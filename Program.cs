using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.WebUtilities;

namespace ShopeeServer
{
    class Program
    {
        // --- 1. HỆ THỐNG LOG VÀO BỘ NHỚ ---
        private static List<string> _serverLogs = new();
        private static object _lock = new();

        public static void Log(string msg)
        {
            try
            {
                string time = DateTime.Now.ToString("HH:mm:ss");
                string logLine = $"[{time}] {msg}";

                // In ra Console
                Console.WriteLine();
                Console.WriteLine(logLine);

                // Lưu vào RAM để hiển thị lên Web
                lock (_lock)
                {
                    _serverLogs.Insert(0, "");
                    _serverLogs.Insert(0, logLine); // Thêm vào đầu
                    if (_serverLogs.Count > 200) _serverLogs.RemoveAt(_serverLogs.Count - 1); // Giới hạn 200 dòng
                }
            }
            catch { }
        }

        private static readonly Regex LocationRegex = new(@"\[(?<Shelf>\d{1,2})N(?<Level>\d)(?:-(?<Box>\d))?\]", RegexOptions.Compiled | RegexOptions.CultureInvariant);

        public static Dictionary<string, string> GetItemLocation(string input)
        {
            var resultData = new Dictionary<string, string>();
            if (string.IsNullOrEmpty(input)) return resultData;
            var match = LocationRegex.Match(input);
            if (match.Success)
            {
                resultData["Shelf"] = match.Groups["Shelf"].Value;
                resultData["Level"] = match.Groups["Level"].Value;
                if (match.Groups["Box"].Success) resultData["Box"] = match.Groups["Box"].Value;
            }
            return resultData;
        }

        private static List<Order> _dbOrders = new();

        static async Task Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            Log("=== SHOPEE WMS SERVER WEB-UI v5.1 (Full & Fixed) ===");

            try
            {
                // 1. CHECK AUTH
                if (string.IsNullOrEmpty(ShopeeApiHelper.AccessToken))
                {
                    Log("[WARN] Chưa có Token. Vui lòng vào Tab 'Hệ thống' trên Web để đăng nhập.");
                }

                // 2. START SYNC (Chạy ngầm)
                _ = Task.Run(async () => {
                    while (true)
                    {
                        try { await CoreEngineSync(); }
                        catch (Exception ex) { Log($"[SyncErr] {ex.Message}"); }
                        await Task.Delay(TimeSpan.FromMinutes(1)); // Đồng bộ mỗi 1 phút
                    }
                });

                // 3. START SERVER
                await StartServer();
            }
            catch (Exception ex)
            {
                Log($"CRITICAL ERROR: {ex}");
            }
            finally
            {
                Console.WriteLine("\nApp đã dừng. Nhấn Enter để thoát...");
                Console.ReadLine();
            }
        }

        // --- 4. LOGIC ĐỒNG BỘ MỚI ---
        static async Task CoreEngineSync()
        {
            if (string.IsNullOrEmpty(ShopeeApiHelper.AccessToken)) return;

            Log("Đang đồng bộ đơn hàng...");
            long to = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            long from = DateTimeOffset.UtcNow.AddDays(-15).ToUnixTimeSeconds();

            // BƯỚC 1: Lấy danh sách ID của đơn "Chưa xử lý" (READY_TO_SHIP)
            var readyIds = await FetchIds("READY_TO_SHIP", from, to);

            // BƯỚC 2: Lấy danh sách ID của đơn "Đã xử lý" (PROCESSED)
            var processedIds = await FetchIds("PROCESSED", from, to);

            if (readyIds == null || processedIds == null) return; // Lỗi mạng hoặc Token

            lock (_lock)
            {
                // DỌN DẸP
                _dbOrders.RemoveAll(o => o.Status == 0 && !readyIds.Contains(o.OrderId));
                _dbOrders.RemoveAll(o => o.Status == 1 && !processedIds.Contains(o.OrderId));
            }

            // C. THÊM MỚI
            var newReady = readyIds.Where(id => !_dbOrders.Any(o => o.OrderId == id)).ToList();
            var newProcessed = processedIds.Where(id => !_dbOrders.Any(o => o.OrderId == id)).ToList();

            if (newReady.Count > 0) await FetchAndAddOrders(newReady, 0);
            if (newProcessed.Count > 0) await FetchAndAddOrders(newProcessed, 1);

            await UpdatePrintingStatus();
        }

        // Helper: Lấy danh sách ID theo trạng thái
        static async Task<List<string>?> FetchIds(string status, long from, long to)
        {
            List<string> allIds = new();
            string cursor = "";
            bool more = true;

            do
            {
                string json = await ShopeeApiHelper.GetOrderList(from, to, status, cursor);

                if (!json.Contains("\"response\""))
                {
                    Log($"Lỗi lấy đơn {status}: {json}");
                    if (await ShopeeApiHelper.RefreshTokenNow())
                    {
                        json = await ShopeeApiHelper.GetOrderList(from, to, status, cursor);
                    }
                    else
                    {
                        return null;
                    }
                }

                using (JsonDocument doc = JsonDocument.Parse(json))
                {
                    if (doc.RootElement.TryGetProperty("response", out var r))
                    {
                        if (r.TryGetProperty("order_list", out var l) && l.ValueKind == JsonValueKind.Array)
                        {
                            foreach (var i in l.EnumerateArray())
                            {
                                string? sn = i.GetProperty("order_sn").GetString();
                                if (!string.IsNullOrEmpty(sn)) allIds.Add(sn);
                            }
                        }

                        more = r.TryGetProperty("more", out var m) && m.GetBoolean();
                        if (more && r.TryGetProperty("next_cursor", out var nc))
                        {
                            cursor = nc.GetString() ?? "";
                        }
                    }
                    else
                    {
                        more = false;
                    }
                }

                if (more)
                {
                    Log($"...Đang tải tiếp trang sau ({allIds.Count} đơn đã tìm thấy)...");
                    await Task.Delay(100);
                }

            } while (more);

            return allIds;
        }

        static async Task FetchAndAddOrders(List<string> ids, int status)
        {
            Log($"Tải chi tiết {ids.Count} đơn mới (Status={status})...");
            for (int i = 0; i < ids.Count; i += 50)
            {
                string snStr = string.Join(",", ids.Skip(i).Take(50));
                string detailJson = await ShopeeApiHelper.GetOrderDetails(snStr);

                try
                {
                    using JsonDocument dDoc = JsonDocument.Parse(detailJson);
                    if (dDoc.RootElement.TryGetProperty("response", out var dr) && dr.TryGetProperty("order_list", out var dl))
                    {
                        lock (_lock)
                        {
                            foreach (var o in dl.EnumerateArray())
                            {
                                var ord = new Order
                                {
                                    OrderId = o.GetProperty("order_sn").GetString()!,
                                    UpdateAt = o.GetProperty("update_time").GetInt64(),
                                    Status = status,
                                    Note = o.TryGetProperty("note", out var n) ? n.GetString() ?? "" : "",
                                    TotalAmount = o.TryGetProperty("total_amount", out var t) ? t.GetDecimal() : 0,
                                    ShippingCarrier = o.TryGetProperty("shipping_carrier", out var sc) ? sc.GetString() ?? "Khác" : "Khác"
                                };
                                foreach (var it in o.GetProperty("item_list").EnumerateArray())
                                {
                                    string name = it.GetProperty("model_name").GetString()!;
                                    Dictionary<string, string> itemLocation = GetItemLocation(name);
                                    ord.Items.Add(new OrderItem
                                    {
                                        ProductId = it.GetProperty("item_id").GetInt64(),
                                        ProductName = it.GetProperty("item_name").GetString()!,
                                        ModelId = it.GetProperty("model_id").GetInt64(),
                                        ModelName = name,
                                        ImageUrl = it.GetProperty("image_info").GetProperty("image_url").GetString()!,
                                        Quantity = it.GetProperty("model_quantity_purchased").GetInt32(),
                                        Price = it.TryGetProperty("model_discounted_price", out var p) ? p.GetDecimal() : 0,
                                        Shelf = itemLocation.ContainsKey("Shelf") ? itemLocation["Shelf"] : null,
                                        Level = itemLocation.ContainsKey("Level") ? itemLocation["Level"] : null,
                                        Box = itemLocation.ContainsKey("Box") ? itemLocation["Box"] : null,
                                    });
                                }
                                if (!_dbOrders.Any(x => x.OrderId == ord.OrderId))
                                    _dbOrders.Add(ord);
                            }
                        }
                    }
                }
                catch (Exception ex) { Log($"[FetchDetail Error] {ex.Message}"); }
            }
        }

        static async Task UpdatePrintingStatus()
        {
            List<string> snsToCheck;
            lock (_lock)
            {
                snsToCheck = _dbOrders.Select(o => o.OrderId).ToList();
            }

            if (snsToCheck.Count == 0) return;

            for (int i = 0; i < snsToCheck.Count; i += 50)
            {
                var batch = snsToCheck.Skip(i).Take(50).ToList();
                try
                {
                    string jsonRes = await ShopeeApiHelper.GetBatchDocResult(batch);
                    using JsonDocument doc = JsonDocument.Parse(jsonRes);
                    if (doc.RootElement.TryGetProperty("response", out var resp) &&
                        resp.TryGetProperty("result_list", out var list))
                    {
                        lock (_lock)
                        {
                            foreach (var item in list.EnumerateArray())
                            {
                                string? sn = item.GetProperty("order_sn").GetString();
                                if (string.IsNullOrEmpty(sn)) continue;

                                string status = item.TryGetProperty("status", out var s) ? s.GetString() : "FAILED";

                                if (status == "READY")
                                {
                                    var order = _dbOrders.FirstOrDefault(o => o.OrderId == sn);
                                    if (order != null) order.Printed = true;
                                }
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Log($"[CheckPrintStatus] Lỗi batch {i}: {ex.Message}");
                }
            }
        }

        static async Task StartServer()
        {
            var listener = new HttpListener();
            listener.Prefixes.Add("http://+:8080/");
            try
            {
                listener.Start();
                Log("Web Server đang chạy tại: http://localhost:8080/");
                try { Process.Start(new ProcessStartInfo("http://localhost:8080/") { UseShellExecute = true }); } catch { }
            }
            catch (Exception ex)
            {
                Log($"LỖI KHỞI ĐỘNG SERVER (Port 8080): {ex.Message}");
                Log("Hãy thử chạy phần mềm với quyền Admin (Run as Administrator).");
                return;
            }

            while (true)
            {
                try
                {
                    var ctx = await listener.GetContextAsync();
                    var req = ctx.Request;
                    var resp = ctx.Response;
                    string url = req.Url.AbsolutePath;

                    // 1. GIAO DIỆN CHÍNH
                    if (url == "/")
                    {
                        string htmlContent = "<h1>Lỗi: Không tìm thấy file index.html</h1>";
                        string filePath = "index.html";

                        if (File.Exists(filePath))
                        {
                            htmlContent = File.ReadAllText(filePath);
                        }
                        else
                        {
                            string debugPath = Path.Combine(Directory.GetCurrentDirectory(), "../../..", "index.html");
                            if (File.Exists(debugPath)) htmlContent = File.ReadAllText(debugPath);
                        }

                        byte[] b = Encoding.UTF8.GetBytes(htmlContent);
                        resp.ContentType = "text/html; charset=utf-8"; resp.OutputStream.Write(b, 0, b.Length);
                    }
                    // 2. API DỮ LIỆU CHÍNH
                    else if (url == "/api/data")
                    {
                        var data = new
                        {
                            orders = _dbOrders,
                            hasToken = !string.IsNullOrEmpty(ShopeeApiHelper.AccessToken),
                            loginUrl = ShopeeApiHelper.GetAuthUrl(),
                        };
                        string j; lock (_lock) { j = JsonSerializer.Serialize(data); }
                        byte[] b = Encoding.UTF8.GetBytes(j);
                        resp.ContentType = "application/json"; resp.OutputStream.Write(b, 0, b.Length);
                    }
                    // 4. API LẤY CHI TIẾT SẢN PHẨM (Logic đầy đủ)
                    else if (url == "/api/product")
                    {
                        string sid = req.QueryString["id"];
                        if (long.TryParse(sid, out long itemId))
                        {
                            string rawJson = ShopeeApiHelper.GetItemModelList(itemId).Result;
                            var result = new { success = false, name = "", variations = new List<object>() };

                            using (JsonDocument doc = JsonDocument.Parse(rawJson))
                            {
                                if (doc.RootElement.TryGetProperty("response", out var r) && r.TryGetProperty("standardise_tier_variation", out var v) && v.GetArrayLength() > 0)
                                {
                                    var v1 = v[0];
                                    var vars = new List<object>();
                                    if (v1.TryGetProperty("variation_option_list", out var vlist))
                                    {
                                        foreach (var m in vlist.EnumerateArray())
                                        {
                                            vars.Add(new { name = m.GetProperty("variation_option_name").GetString(), stock = "stock", img = m.GetProperty("image_url").GetString() });
                                        }
                                    }
                                    result = new { success = true, name = "iName", variations = vars };
                                }
                            }
                            byte[] b = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(result));
                            resp.ContentType = "application/json"; resp.OutputStream.Write(b, 0, b.Length);
                        }
                    }
                    // 5. API MỚI: LẤY LOG HỆ THỐNG
                    else if (url == "/api/logs")
                    {
                        string j; lock (_lock) { j = JsonSerializer.Serialize(_serverLogs); }
                        byte[] b = Encoding.UTF8.GetBytes(j);
                        resp.ContentType = "application/json"; resp.OutputStream.Write(b, 0, b.Length);
                    }
                    // 6. API MỚI: XỬ LÝ LOGIN TỪ WEB
                    else if (url == "/api/login" && req.HttpMethod == "POST")
                    {
                        using var reader = new StreamReader(req.InputStream, req.ContentEncoding);
                        string body = await reader.ReadToEndAsync();

                        try
                        {
                            string fullUrl = body;
                            if (body.StartsWith("{"))
                            {
                                using JsonDocument tmp = JsonDocument.Parse(body);
                                if (tmp.RootElement.TryGetProperty("url", out var u)) fullUrl = u.GetString();
                            }

                            if (!fullUrl.Contains("code=")) throw new Exception("Link không chứa mã code!");

                            var uri = new Uri(fullUrl);
                            var q = QueryHelpers.ParseQuery(uri.Query);
                            long sid = long.Parse(q["shop_id"]!);
                            string code = q["code"]!;

                            Log($"Đang xử lý đăng nhập cho Shop ID: {sid}...");
                            bool ok = await ShopeeApiHelper.ExchangeCodeForToken(sid, code);

                            string msg = ok ? "{\"success\":true}" : "{\"success\":false, \"message\":\"Lỗi Shopee API (Check Log)\"}";
                            if (ok) Log("=> Đăng nhập thành công! Token đã được lưu."); else Log("=> Đăng nhập thất bại.");

                            byte[] b = Encoding.UTF8.GetBytes(msg);
                            resp.ContentType = "application/json"; resp.OutputStream.Write(b, 0, b.Length);
                        }
                        catch (Exception ex)
                        {
                            Log($"Lỗi Login Web: {ex.Message}");
                            byte[] b = Encoding.UTF8.GetBytes("{\"success\":false, \"message\":\"Lỗi xử lý link\"}");
                            resp.OutputStream.Write(b, 0, b.Length);
                        }
                    }
                    // 7. API SHIP ĐƠN HÀNG (SỬA LỖI shipSn bị NULL)
                    else if (url == "/api/ship" && req.HttpMethod == "POST")
                    {
                        string shipSn = req.QueryString["id"] ?? "";

                        // --> [FIX] Kiểm tra null ngay lập tức
                        if (string.IsNullOrEmpty(shipSn))
                        {
                            Log("[SHIP ERROR] Client gọi API nhưng không gửi ID đơn hàng (shipSn is null)");
                            byte[] err = Encoding.UTF8.GetBytes("MISSING_ID");
                            resp.StatusCode = 400; resp.OutputStream.Write(err, 0, err.Length);
                            resp.Close();
                            continue;
                        }

                        Log($"[SHIP] Bắt đầu xử lý đơn: {shipSn}");

                        try
                        {
                            string paramJson = await ShopeeApiHelper.GetShippingParam(shipSn);
                            Log($"[DEBUG-SHIP] Param: {paramJson}");

                            object shipPayload = null;
                            using (var doc = JsonDocument.Parse(paramJson))
                            {
                                if (doc.RootElement.TryGetProperty("response", out var r))
                                {
                                    // A. Pickup
                                    if (r.TryGetProperty("pickup", out var pickupData) &&
                                        pickupData.TryGetProperty("address_list", out var addrList) &&
                                        addrList.GetArrayLength() > 0)
                                    {
                                        var firstAddr = addrList[0];
                                        long addrId = firstAddr.GetProperty("address_id").GetInt64();
                                        string timeId = "";

                                        if (firstAddr.TryGetProperty("time_slot_list", out var timeList) && timeList.GetArrayLength() > 0)
                                            timeId = timeList[0].GetProperty("pickup_time_id").GetString();

                                        shipPayload = new
                                        {
                                            order_sn = shipSn,
                                            pickup = new { address_id = addrId, pickup_time_id = timeId }
                                        };
                                        Log($"-> Mode: PICKUP (Addr: {addrId} | Time: {timeId})");
                                    }
                                    // B. Dropoff
                                    else
                                    {
                                        shipPayload = new { order_sn = shipSn, dropoff = new { } };
                                        Log("-> Mode: DROPOFF");
                                    }
                                }
                            }

                            if (shipPayload != null)
                            {
                                string shipRes = await ShopeeApiHelper.ShipOrder(shipPayload);
                                if (!shipRes.Contains("error"))
                                {
                                    Log($"[SHIP OK] Thành công!");
                                    // Cập nhật trạng thái RAM
                                    lock (_lock) { var o = _dbOrders.FirstOrDefault(x => x.OrderId == shipSn); if (o != null) o.Status = 1; }
                                    _ = Task.Run(() => CoreEngineSync());
                                }
                                else
                                {
                                    Log($"[SHIP ERROR] {shipRes}");
                                }
                            }
                            else
                            {
                                Log("[SHIP LỖI] Không xác định được phương thức Pickup/Dropoff");
                            }
                        }
                        catch (Exception apiEx)
                        {
                            Log($"[SHIP EXCEPTION] {apiEx.Message}");
                        }
                        byte[] ok = Encoding.UTF8.GetBytes("DONE"); resp.OutputStream.Write(ok, 0, ok.Length);
                    }
                    // 8. API NOTE
                    else if (url == "/api/note" && req.HttpMethod == "POST")
                    {
                        string id = req.QueryString["id"];
                        using var reader = new StreamReader(req.InputStream, req.ContentEncoding);
                        string content = await reader.ReadToEndAsync();

                        lock (_lock)
                        {
                            var o = _dbOrders.FirstOrDefault(x => x.OrderId == id);
                            if (o != null) o.Note = content;
                        }

                        Log($"[NOTE] Đơn {id}: {content}");
                        resp.StatusCode = 200;
                        byte[] b = Encoding.UTF8.GetBytes("OK");
                        resp.OutputStream.Write(b, 0, b.Length);
                    }
                    // 9. API MANUAL SYNC
                    else if (url == "/api/sync" && req.HttpMethod == "POST")
                    {
                        Log("[MANUAL] Kích hoạt đồng bộ từ Client...");
                        _ = Task.Run(() => CoreEngineSync());
                        byte[] b = Encoding.UTF8.GetBytes("{}"); resp.OutputStream.Write(b, 0, b.Length);
                    }
                    // 10. API UPDATE PICKER & STATUS
                    else if (url == "/api/update-batch" && req.HttpMethod == "POST")
                    {
                        using var reader = new StreamReader(req.InputStream, req.ContentEncoding);
                        string json = await reader.ReadToEndAsync();

                        var data = JsonSerializer.Deserialize<BatchUpdateReq>(json);

                        if (data != null && data.Ids != null)
                        {
                            lock (_lock)
                            {
                                var targets = _dbOrders.Where(o => data.Ids.Contains(o.OrderId)).ToList();
                                foreach (var o in targets)
                                {
                                    if (data.Field == "picker") o.Picker = data.Value;
                                    if (data.Field == "status") o.PickingStatus = data.Value;
                                }
                            }
                            Log($"[UPDATE] Đã cập nhật {data.Field}='{data.Value}' cho {data.Ids.Count} đơn.");
                            byte[] b = Encoding.UTF8.GetBytes("OK");
                            resp.OutputStream.Write(b, 0, b.Length);
                        }
                    }
                    // 11.API XỬ LÝ IN ĐƠN (SỬA LỖI printSn bị NULL)
                    else if (url == "/api/print" && req.HttpMethod == "POST")
                    {
                        using var reader = new StreamReader(req.InputStream, req.ContentEncoding);
                        string json = await reader.ReadToEndAsync();

                        // --> [FIX] Kiểm tra Body JSON và danh sách Ids
                        BatchUpdateReq? data = null;
                        try
                        {
                            data = JsonSerializer.Deserialize<BatchUpdateReq>(json);
                        }
                        catch { }

                        if (data == null || data.Ids == null || data.Ids.Count == 0)
                        {
                            Log("[PRINT ERROR] Client gọi in nhưng không gửi ID nào.");
                            byte[] err = Encoding.UTF8.GetBytes("{\"success\":false, \"message\":\"No IDs provided\"}");
                            resp.ContentType = "application/json"; resp.OutputStream.Write(err, 0, err.Length);
                            resp.Close();
                            continue;
                        }

                        var processedIds = new List<string>();
                        var errorIds = new List<string>();

                        string tempPath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "temp");
                        if (!Directory.Exists(tempPath)) Directory.CreateDirectory(tempPath);

                        string printerTool = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "SumatraPDF.exe");
                        if (!File.Exists(printerTool))
                        {
                            Log("[WARN] Không tìm thấy file SumatraPDF.exe, hệ thống chỉ tải file PDF về.");
                        }

                        foreach (var printSn in data.Ids ?? new List<string>())
                        {
                            if (string.IsNullOrEmpty(printSn)) continue;
                            Log($"[PRINT] Đang xử lý đơn: {printSn}...");

                            // 1. Chờ Shopee tạo file (Polling)
                            bool isReady = false;
                            for (int i = 0; i < 15; i++)
                            {
                                string resStatus = await ShopeeApiHelper.GetDocResult(printSn);
                                string status = "FAILED";
                                try
                                {
                                    using (JsonDocument doc = JsonDocument.Parse(resStatus))
                                    {
                                        if (doc.RootElement.TryGetProperty("response", out var r) &&
                                           r.TryGetProperty("result_list", out var l) && l.GetArrayLength() > 0)
                                        {
                                            status = l[0].TryGetProperty("status", out var s) ? s.GetString() : "FAILED";
                                        }
                                    }
                                }
                                catch { }

                                if (status == "READY") { isReady = true; break; }

                                // [LOGIC MỚI] Nếu chưa có file (PROCESSING/FAILED), thử tạo lại kèm Tracking Number
                                if (status != "PROCESSING")
                                {
                                    // B1: Lấy Tracking Number
                                    string trackJson = await ShopeeApiHelper.GetTrackingNumber(printSn);
                                    string trackingNum = "";
                                    try
                                    {
                                        using (JsonDocument tDoc = JsonDocument.Parse(trackJson))
                                        {
                                            if (tDoc.RootElement.TryGetProperty("response", out var tr) &&
                                                tr.TryGetProperty("tracking_number", out var tVal))
                                            {
                                                trackingNum = tVal.GetString() ?? "";
                                            }
                                        }
                                    }
                                    catch { }

                                    // B2: Gọi lệnh tạo Doc với Tracking Number
                                    if (!string.IsNullOrEmpty(trackingNum))
                                    {
                                        await ShopeeApiHelper.CreateDoc(printSn, trackingNum);
                                    }
                                    else
                                    {
                                        Log($"[WARN] Không lấy được Tracking Number cho {printSn}");
                                    }
                                }

                                await Task.Delay(1500);
                            }

                            // ... (Phần code tải file và in giữ nguyên như cũ) ...
                            if (!isReady) { errorIds.Add(printSn); continue; }

                            // (Copy lại phần tải file và in của bạn vào đây, không thay đổi gì)
                            string filePath = Path.Combine(tempPath, $"{printSn}.pdf");
                            if (!File.Exists(filePath))
                            { /* Logic tải file cũ */
                                byte[] pdfBytes = await ShopeeApiHelper.DownloadDoc(printSn);
                                if (pdfBytes.Length > 0) await File.WriteAllBytesAsync(filePath, pdfBytes);
                                else { errorIds.Add(printSn); continue; }
                            }

                            // Logic gọi SumatraPDF cũ...
                            if (File.Exists(printerTool))
                            {
                                // ... Code in cũ ...
                                try
                                {
                                    var p = new System.Diagnostics.Process();
                                    p.StartInfo.FileName = printerTool;
                                    // Thêm -print-settings "fit" để tự động co dãn vừa khổ giấy
                                    p.StartInfo.Arguments = $"-print-to-default -silent -print-settings \"fit\" \"{filePath}\"";
                                    p.StartInfo.CreateNoWindow = true;
                                    p.StartInfo.UseShellExecute = false;
                                    p.Start();
                                    lock (_lock) { var o = _dbOrders.FirstOrDefault(x => x.OrderId == printSn); if (o != null) o.Printed = true; }
                                    processedIds.Add(printSn);
                                    Log($"[PRINT OK] Đã in đơn {printSn}");
                                }
                                catch (Exception ex) { Log($"[PRINT ERROR] {ex.Message}"); errorIds.Add(printSn); }
                            }
                        }

                        var result = new { success = true, processed = processedIds, errors = errorIds };
                        byte[] b = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(result));
                        resp.ContentType = "application/json";
                        resp.OutputStream.Write(b, 0, b.Length);
                    }
                    resp.Close();
                }
                catch (Exception ex)
                {
                    Log($"[SERVER ERR] {ex.Message}");
                }
            }
        }
        public class PrintReqDto
        {
            public List<string> Ids { get; set; } = new List<string>();
        }
    }
}