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
        private static object _lock = new();

        static async Task Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            Log("=== SHOPEE WMS SERVER WEB-UI v5.0 ===");

            try
            {
                // 1. CHECK AUTH: Không chặn màn hình đen nữa, chỉ cảnh báo log
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

        // --- 4. LOGIC ĐỒNG BỘ MỚI (2 TRẠNG THÁI) ---
        static async Task CoreEngineSync()
        {
            if (string.IsNullOrEmpty(ShopeeApiHelper.AccessToken)) return;

            Log("Đang đồng bộ đơn hàng...");
            long to = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            long from = DateTimeOffset.UtcNow.AddDays(-15).ToUnixTimeSeconds();

            // BƯỚC 1: Lấy danh sách ID của đơn "Chưa xử lý" (READY_TO_SHIP)
            var readyIds = await FetchIds("READY_TO_SHIP", from, to);

            // BƯỚC 2: Lấy danh sách ID của đơn "Đã xử lý" (PROCESSED)
            // Lưu ý: Nếu 'PROCESSED' không chạy, hãy thử đổi thành 'SHIPPED'
            var processedIds = await FetchIds("PROCESSED", from, to);

            if (readyIds == null || processedIds == null) return; // Lỗi mạng hoặc Token

            lock (_lock)
            {
                // DỌN DẸP
                _dbOrders.RemoveAll(o => o.Status == 0 && !readyIds.Contains(o.OrderId));
                _dbOrders.RemoveAll(o => o.Status == 1 && !processedIds.Contains(o.OrderId));
            }

            // C. THÊM MỚI: Tải chi tiết cho những đơn chưa có trong RAM
            var newReady = readyIds.Where(id => !_dbOrders.Any(o => o.OrderId == id)).ToList();
            var newProcessed = processedIds.Where(id => !_dbOrders.Any(o => o.OrderId == id)).ToList();

            if (newReady.Count > 0) await FetchAndAddOrders(newReady, 0);       // Thêm vào thẻ "Chưa xử lý"
            if (newProcessed.Count > 0) await FetchAndAddOrders(newProcessed, 1); // Thêm vào thẻ "Đã xử lý"

            await UpdatePrintingStatus();
        }

        // Helper: Lấy danh sách ID theo trạng thái
        static async Task<List<string>?> FetchIds(string status, long from, long to)
        {
            List<string> allIds = new();
            string cursor = ""; // Con trỏ trang, bắt đầu là rỗng
            bool more = true;   // Cờ báo còn trang tiếp theo hay không

            do
            {
                // 1. Gọi API với cursor hiện tại
                string json = await ShopeeApiHelper.GetOrderList(from, to, status, cursor);

                // 2. Xử lý lỗi Token hết hạn (giống code cũ của bạn)
                if (!json.Contains("\"response\""))
                {
                    Log($"Lỗi lấy đơn {status}: {json}");
                    if (await ShopeeApiHelper.RefreshTokenNow())
                    {
                        // Thử lại lần nữa nếu refresh thành công
                        json = await ShopeeApiHelper.GetOrderList(from, to, status, cursor);
                    }
                    else
                    {
                        return null; // Token lỗi hẳn thì dừng
                    }
                }

                // 3. Parse JSON để lấy ID và Cursor mới
                using (JsonDocument doc = JsonDocument.Parse(json))
                {
                    if (doc.RootElement.TryGetProperty("response", out var r))
                    {
                        // Lấy danh sách ID trong trang này
                        if (r.TryGetProperty("order_list", out var l) && l.ValueKind == JsonValueKind.Array)
                        {
                            foreach (var i in l.EnumerateArray())
                            {
                                allIds.Add(i.GetProperty("order_sn").GetString()!);
                            }
                        }

                        // Kiểm tra xem còn trang sau không
                        more = r.TryGetProperty("more", out var m) && m.GetBoolean();

                        // Lấy cursor cho trang tiếp theo
                        if (more && r.TryGetProperty("next_cursor", out var nc))
                        {
                            cursor = nc.GetString() ?? "";
                        }
                    }
                    else
                    {
                        // Nếu không có response hợp lệ thì dừng vòng lặp
                        more = false;
                    }
                }

                // Log nhẹ để biết tiến độ (nếu tải nhiều)
                if (more)
                {
                    Log($"...Đang tải tiếp trang sau ({allIds.Count} đơn đã tìm thấy)...");
                    await Task.Delay(100); // Nghỉ 1 xíu để không spam API
                }

            } while (more); // Lặp lại nếu Shopee báo còn dữ liệu (more = true)

            return allIds;
        }

        // Helper: Tải chi tiết và thêm vào RAM
        static async Task FetchAndAddOrders(List<string> ids, int status)
        {
            Log($"Tải chi tiết {ids.Count} đơn mới (Status={status})...");
            for (int i = 0; i < ids.Count; i += 50)
            {
                string snStr = string.Join(",", ids.Skip(i).Take(50));
                string detailJson = await ShopeeApiHelper.GetOrderDetails(snStr);
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
                                Status = status,// <--- Gán trạng thái 0 hoặc 1 tùy nguồn
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
                                    ItemId = it.GetProperty("item_id").GetInt64(),
                                    ProductName = it.GetProperty("item_name").GetString()!,
                                    ModelName = name,
                                    ImageUrl = it.GetProperty("image_info").GetProperty("image_url").GetString()!,
                                    Quantity = it.GetProperty("model_quantity_purchased").GetInt32(),
                                    Price = it.TryGetProperty("model_discounted_price", out var p) ? p.GetDecimal() : 0,
                                    Shelf = itemLocation.ContainsKey("Shelf") ? itemLocation["Shelf"] : null,
                                    Level = itemLocation.ContainsKey("Level") ? itemLocation["Level"] : null,
                                    Box = itemLocation.ContainsKey("Box") ? itemLocation["Box"] : null,
                                });
                            }
                            // Kiểm tra lần cuối để tránh trùng lặp
                            if (!_dbOrders.Any(x => x.OrderId == ord.OrderId))
                                _dbOrders.Add(ord);
                        }
                    }
                }
            }
        }

        static async Task UpdatePrintingStatus()
        {
            List<string> snsToCheck;
            lock (_lock)
            {
                // Chỉ kiểm tra những đơn đã có Mã vận đơn
                // Bạn có thể bỏ điều kiện !o.Printed nếu muốn check lại cả những đơn đã in
                snsToCheck = _dbOrders
                    .Select(o => o.OrderId)
                    .ToList();
            }

            if (snsToCheck.Count == 0) return;

            // Chia nhỏ mỗi lần check 50 đơn để tránh lỗi API quá tải
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
                                string sn = item.GetProperty("order_sn").GetString();
                                string status = item.TryGetProperty("status", out var s) ? s.GetString() : "FAILED";

                                // Status có thể là: PROCESSING, READY, FAILED
                                if (status == "READY")
                                {
                                    var order = _dbOrders.FirstOrDefault(o => o.OrderId == sn);
                                    if (order != null)
                                    {
                                        order.Printed = true; // Đánh dấu là Đã in (File đã sẵn sàng)
                                    }
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
                        //byte[] b = Encoding.UTF8.GetBytes(HtmlTemplates.Index);
                        string htmlContent = "<h1>Lỗi: Không tìm thấy file index.html</h1>";
                        string filePath = "index.html"; // File nằm cùng thư mục exe

                        if (File.Exists(filePath))
                        {
                            htmlContent = File.ReadAllText(filePath);
                        }
                        else
                        {
                            // Fallback: Tìm thử ở thư mục gốc project (trường hợp đang debug trong VS)
                            // Đường dẫn này tùy thuộc cấu trúc folder của bạn
                            string debugPath = Path.Combine(Directory.GetCurrentDirectory(), "../../..", "index.html");
                            if (File.Exists(debugPath)) htmlContent = File.ReadAllText(debugPath);
                        }

                        byte[] b = Encoding.UTF8.GetBytes(htmlContent);

                        resp.ContentType = "text/html; charset=utf-8"; resp.OutputStream.Write(b, 0, b.Length);
                    }
                    // 2. API DỮ LIỆU CHÍNH (Kèm trạng thái Login)
                    else if (url == "/api/data")
                    {
                        var data = new
                        {
                            orders = _dbOrders,
                            hasToken = !string.IsNullOrEmpty(ShopeeApiHelper.AccessToken),
                            loginUrl = ShopeeApiHelper.GetAuthUrl(),
                            //carrierLogos = _config.CarrierLogos
                        };
                        string j; lock (_lock) { j = JsonSerializer.Serialize(data); }
                        byte[] b = Encoding.UTF8.GetBytes(j);
                        resp.ContentType = "application/json"; resp.OutputStream.Write(b, 0, b.Length);
                    }

                    // 4. API LẤY CHI TIẾT SẢN PHẨM (Để xem ảnh to/tồn kho)
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
                                    //string iName = item.GetProperty("item_name").GetString()!;
                                    //string defImg = item.GetProperty("image").GetProperty("image_url_list")[0].GetString()!;

                                    var vars = new List<object>();
                                    if (v1.TryGetProperty("variation_option_list", out var vlist))
                                    {
                                        foreach (var m in vlist.EnumerateArray())
                                        {
                                            //int stock = 0;
                                            // Logic lấy Stock V2: stock_info_v2 -> summary_info -> total_available_stock
                                            //if (m.TryGetProperty("stock_info_v2", out var si) && si.TryGetProperty("summary_info", out var sum))
                                            //    stock = sum.GetProperty("total_available_stock").GetInt32();
                                            // Fallback V1
                                            //else if (m.TryGetProperty("stock_info", out var oldSi) && oldSi.GetArrayLength() > 0)
                                            //    stock = oldSi[0].GetProperty("normal_stock").GetInt32();

                                            vars.Add(new { name = m.GetProperty("variation_option_name").GetString(), stock = "stock", img = m.GetProperty("image_url").GetString() });
                                        }
                                    }
                                    else
                                    {
                                        //int stock = 0;
                                        //if (v1.TryGetProperty("stock_info_v2", out var si) && si.TryGetProperty("summary_info", out var sum))
                                        //    stock = sum.GetProperty("total_available_stock").GetInt32();
                                        //vars.Add(new { name = "Mặc định", stock = stock, img = defImg });
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
                            // Parse JSON đơn giản nếu body gửi lên là {"url": "..."}
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
                    // 7. API SHIP ĐƠN HÀNG
                    else if (url == "/api/ship" && req.HttpMethod == "POST")
                    {
                        string sn = req.QueryString["id"];
                        Log($"[SHIP] Bắt đầu xử lý đơn: {sn}");

                        try
                        {
                            string paramJson = await ShopeeApiHelper.GetShippingParam(sn);
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
                                            order_sn = sn,
                                            pickup = new { address_id = addrId, pickup_time_id = timeId }
                                        };
                                        Log($"-> Mode: PICKUP (Addr: {addrId} | Time: {timeId})");
                                    }
                                    // B. Dropoff
                                    else
                                    {
                                        shipPayload = new { order_sn = sn, dropoff = new { } };
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
                                    lock (_lock) { var o = _dbOrders.FirstOrDefault(x => x.OrderId == sn); if (o != null) o.Status = 1; }
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

                        Log($"[NOTE] Đơn {id}: {content}"); // Ghi log để bạn biết có hoạt động
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
                        
                        // Định nghĩa class tạm để hứng dữ liệu
                        var data = JsonSerializer.Deserialize<BatchUpdateReq>(json); // Xem định nghĩa class ở dưới cùng file này
                        
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
                    // 11. API XỬ LÝ IN ĐƠN
                    //else if (url == "/api/print" && req.HttpMethod == "POST")
                    //{
                    //    using var reader = new StreamReader(req.InputStream, req.ContentEncoding);
                    //    var body = await reader.ReadToEndAsync();
                    //    var reqData = JsonSerializer.Deserialize<BatchUpdateReq>(body);

                    //    var processedIds = new List<string>();
                    //    var errorIds = new List<string>();

                    //    if (reqData != null && reqData.Ids != null)
                    //    {
                    //        string tempPath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "temp");
                    //        if (!Directory.Exists(tempPath)) Directory.CreateDirectory(tempPath);

                    //        // Kiểm tra công cụ in
                    //        string printerTool = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "SumatraPDF.exe");
                    //        if (!File.Exists(printerTool))
                    //        {
                    //            var err = new { success = false, message = "Lỗi Server: Thiếu file SumatraPDF.exe" };
                    //            byte[] eb = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(err));
                    //            resp.ContentType = "application/json";
                    //            resp.OutputStream.Write(eb, 0, eb.Length);
                    //            resp.Close();
                    //            continue;
                    //        }

                    //        foreach (var sn in reqData.Ids)
                    //        {
                    //            Log($"[PRINT] Đang xử lý đơn: {sn}...");

                    //            // 1. Chờ Shopee tạo file (Polling)
                    //            bool isReady = false;
                    //            for (int i = 0; i < 15; i++)
                    //            {
                    //                string resStatus = await ShopeeApiHelper.GetDocResult(sn);
                    //                string status = "FAILED";
                    //                using (JsonDocument doc = JsonDocument.Parse(resStatus))
                    //                {
                    //                    if (doc.RootElement.TryGetProperty("response", out var r) &&
                    //                       r.TryGetProperty("result_list", out var l) && l.GetArrayLength() > 0)
                    //                    {
                    //                        status = l[0].TryGetProperty("status", out var s) ? s.GetString() : "FAILED";
                    //                    }
                    //                }

                    //                if (status == "READY") { isReady = true; break; }
                    //                if (status != "PROCESSING") await ShopeeApiHelper.CreateDoc(sn); // Nếu chưa có thì tạo
                    //                await Task.Delay(1500);
                    //            }

                    //            if (!isReady) { errorIds.Add(sn); continue; }

                    //            // 2. Tải file về
                    //            string filePath = Path.Combine(tempPath, $"{sn}.pdf");
                    //            if (!File.Exists(filePath))
                    //            {
                    //                byte[] pdfBytes = await ShopeeApiHelper.DownloadDoc(sn);
                    //                if (pdfBytes.Length > 0) await File.WriteAllBytesAsync(filePath, pdfBytes);
                    //                else { errorIds.Add(sn); continue; }
                    //            }

                    //            // 3. GỌI LỆNH IN TRÊN SERVER (In ngầm)
                    //            try
                    //            {
                    //                var p = new System.Diagnostics.Process();
                    //                p.StartInfo.FileName = printerTool;
                    //                // -print-to-default: In ra máy in mặc định
                    //                // -silent: Không hiện cửa sổ
                    //                p.StartInfo.Arguments = $"-print-to-default -silent \"{filePath}\"";
                    //                p.StartInfo.CreateNoWindow = true;
                    //                p.StartInfo.UseShellExecute = false;
                    //                p.Start();

                    //                // Cập nhật trạng thái
                    //                lock (_lock)
                    //                {
                    //                    var o = _dbOrders.FirstOrDefault(x => x.OrderId == sn);
                    //                    if (o != null) o.Printed = true;
                    //                }
                    //                processedIds.Add(sn);
                    //                Log($"[PRINT OK] Đã in đơn {sn}");
                    //            }
                    //            catch (Exception ex)
                    //            {
                    //                Log($"[PRINT ERROR] {ex.Message}");
                    //                errorIds.Add(sn);
                    //            }
                    //        }
                    //    }

                    //    var result = new { success = true, processed = processedIds, errors = errorIds };
                    //    byte[] b = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(result));
                    //    resp.ContentType = "application/json";
                    //    resp.OutputStream.Write(b, 0, b.Length);
                    //}
                    else if (url == "/api/print" && req.HttpMethod == "POST")
                    {
                        try
                        {
                            using var reader = new StreamReader(req.InputStream, req.ContentEncoding);
                            var body = await reader.ReadToEndAsync();

                            // [DEBUG] In ra body nhận được để kiểm tra
                            Log($"[API PRINT BODY]: {body}");

                            // 1. Cấu hình bỏ qua phân biệt hoa thường (FIX LỖI NULL)
                            var options = new JsonSerializerOptions
                            {
                                PropertyNameCaseInsensitive = true,
                                ReadCommentHandling = JsonCommentHandling.Skip,
                                AllowTrailingCommas = true
                            };

                            BatchUpdateReq reqData = JsonSerializer.Deserialize<BatchUpdateReq>(body);

                            var processedIds = new List<string>();
                            var errorIds = new List<string>();

                            // 2. Kiểm tra Null an toàn hơn
                            if (reqData != null && reqData.Ids != null && reqData.Ids.Count > 0)
                            {
                                string tempPath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "temp");
                                if (!Directory.Exists(tempPath)) Directory.CreateDirectory(tempPath);

                                string printerTool = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "SumatraPDF.exe");
                                if (!File.Exists(printerTool))
                                {
                                    // Log lỗi nếu thiếu tool
                                    Log("[ERROR] Không tìm thấy SumatraPDF.exe");
                                    var err = new { success = false, message = "Server: Thiếu file SumatraPDF.exe" };
                                    byte[] eb = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(err));
                                    resp.ContentType = "application/json";
                                    resp.OutputStream.Write(eb, 0, eb.Length);
                                    resp.Close();
                                    continue;
                                }

                                foreach (var sn in reqData.Ids)
                                {
                                    if (string.IsNullOrEmpty(sn)) continue; // Bỏ qua nếu sn rỗng

                                    Log($"[PRINT] Đang xử lý đơn: {sn}...");

                                    // --- VÒNG LẶP CHỜ SHOPEE TẠO FILE ---
                                    bool isReady = false;
                                    for (int i = 0; i < 15; i++)
                                    {
                                        string resStatus = await ShopeeApiHelper.GetDocResult(sn);
                                        string status = "FAILED";

                                        // Parse JSON an toàn
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

                                        // Nếu chưa có (NOT_EXIST) hoặc đang xử lý -> Gửi lệnh tạo
                                        if (status != "PROCESSING" && status != "READY")
                                        {
                                            await ShopeeApiHelper.CreateDoc(sn);
                                        }

                                        await Task.Delay(1500);
                                    }

                                    if (!isReady)
                                    {
                                        Log($"[PRINT TIMEOUT] Đơn {sn} Shopee chưa trả về PDF.");
                                        errorIds.Add(sn);
                                        continue;
                                    }

                                    // --- TẢI FILE ---
                                    string filePath = Path.Combine(tempPath, $"{sn}.pdf");
                                    if (!File.Exists(filePath))
                                    {
                                        byte[] pdfBytes = await ShopeeApiHelper.DownloadDoc(sn);
                                        if (pdfBytes != null && pdfBytes.Length > 0)
                                        {
                                            await File.WriteAllBytesAsync(filePath, pdfBytes);
                                        }
                                        else
                                        {
                                            errorIds.Add(sn);
                                            continue;
                                        }
                                    }

                                    // --- IN ---
                                    try
                                    {
                                        // Thay tên máy in của bạn vào đây nếu cần, hoặc để default
                                        // string printerName = "Xprinter XP-350B"; 
                                        // string args = $"-print-to \"{printerName}\" -silent \"{filePath}\"";

                                        string args = $"-print-to-default -silent \"{filePath}\"";

                                        var p = new System.Diagnostics.Process();
                                        p.StartInfo.FileName = printerTool;
                                        p.StartInfo.Arguments = args;
                                        p.StartInfo.CreateNoWindow = true;
                                        p.StartInfo.UseShellExecute = false;
                                        p.Start();

                                        lock (_lock)
                                        {
                                            var o = _dbOrders.FirstOrDefault(x => x.OrderId == sn);
                                            if (o != null) o.Printed = true;
                                        }
                                        processedIds.Add(sn);
                                        Log($"[PRINT OK] Đã in đơn {sn}");
                                    }
                                    catch (Exception ex)
                                    {
                                        Log($"[PRINT ERROR] {ex.Message}");
                                        errorIds.Add(sn);
                                    }
                                }
                            }
                            else
                            {
                                Log("[API ERROR] reqData hoặc reqData.Ids bị null!");
                            }

                            var result = new { success = true, processed = processedIds, errors = errorIds };
                            byte[] b = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(result));
                            resp.ContentType = "application/json";
                            resp.OutputStream.Write(b, 0, b.Length);
                        }
                        catch (Exception ex)
                        {
                            Log($"[CRITICAL ERROR] /api/print: {ex.Message} {ex.StackTrace}");
                            resp.StatusCode = 500;
                        }
                    }
                    resp.Close();
                }
                catch (Exception ex)
                {
                    Log($"[SERVER ERR] {ex.Message}");
                }
            }
        }
    }
}