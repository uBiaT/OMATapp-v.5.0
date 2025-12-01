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
        private static readonly Regex LocationRegex = new Regex(@"^\[(?<Shelf>\d+)N(?<Level>\d+)(?:-(?<Box>\d+))?\]", RegexOptions.Compiled | RegexOptions.CultureInvariant);
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

        private static List<Order> _dbOrders = new List<Order>();
        private static object _lock = new object();

        static async Task Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            Console.WriteLine("=== SHOPEE WMS SERVER v4.1 (Debug Mode) ===");

            try
            {
                // 1. CHECK AUTH
                if (string.IsNullOrEmpty(ShopeeApiHelper.AccessToken))
                {
                    Console.WriteLine("\n[WARN] Chưa có Token. Vui lòng Login:");
                    Console.WriteLine(ShopeeApiHelper.GetAuthUrl());
                    Console.Write("Callback URL: ");
                    string cb = Console.ReadLine()?.Trim() ?? "";
                    if (!string.IsNullOrEmpty(cb))
                    {
                        var q = QueryHelpers.ParseQuery(new Uri(cb).Query);
                        long sid = long.Parse(q["shop_id"]!);
                        if (await ShopeeApiHelper.ExchangeCodeForToken(sid, q["code"]!))
                            Console.WriteLine("[OK] Đã lưu Token.");
                        else
                        {
                            Console.WriteLine("Lỗi Login. Nhấn Enter để thoát...");
                            Console.ReadLine();
                            return;
                        }
                    }
                    else return;
                }

                // 2. START SYNC
                _ = Task.Run(async () => {
                    while (true)
                    {
                        try { await CoreEngineSync(); }
                        catch (Exception ex) { Console.WriteLine($"[SyncErr] {ex.Message}"); }
                        await Task.Delay(TimeSpan.FromMinutes(1));
                    }
                });

                // 3. START SERVER
                // --- SỬA ĐỔI 2: Dùng await để giữ Main không thoát ---
                await StartServer();
            }
            catch (Exception ex)
            {
                // --- SỬA ĐỔI 3: Bắt lỗi Crash ---
                Console.WriteLine("\n\n*********************************");
                Console.WriteLine("CRITICAL ERROR (LỖI NGHIÊM TRỌNG):");
                Console.WriteLine(ex.ToString());
                Console.WriteLine("*********************************");
            }
            finally
            {
                // --- SỬA ĐỔI 4: Giữ cửa sổ luôn mở ---
                Console.WriteLine("\nApp đã dừng. Nhấn Enter để thoát...");
                Console.ReadLine();
            }
        }

        static async Task CoreEngineSync()
        {
            Console.WriteLine($"[{DateTime.Now:HH:mm}] Syncing...");
            long to = DateTimeOffset.UtcNow.ToUnixTimeSeconds(), from = DateTimeOffset.UtcNow.AddDays(-15).ToUnixTimeSeconds();

            string json = await ShopeeApiHelper.GetOrderList(from, to);
            if (!json.Contains("\"response\""))
            {
                if (await ShopeeApiHelper.RefreshTokenNow()) json = await ShopeeApiHelper.GetOrderList(from, to);
                else return;
            }

            List<string> liveIds = new List<string>();
            using (JsonDocument doc = JsonDocument.Parse(json))
            {
                if (doc.RootElement.TryGetProperty("response", out var r) && r.TryGetProperty("order_list", out var l))
                    foreach (var i in l.EnumerateArray()) liveIds.Add(i.GetProperty("order_sn").GetString()!);
            }

            lock (_lock) { _dbOrders.RemoveAll(o => o.Status == 0 && !liveIds.Contains(o.OrderId)); }

            List<string> newIds;
            lock (_lock) { newIds = liveIds.Where(id => !_dbOrders.Any(o => o.OrderId == id)).ToList(); }

            if (newIds.Count > 0)
            {
                Console.WriteLine($"Phát hiện {newIds.Count} đơn mới.");
                for (int i = 0; i < newIds.Count; i += 50)
                {
                    string snStr = string.Join(",", newIds.Skip(i).Take(50));
                    string detailJson = await ShopeeApiHelper.GetOrderDetails(snStr);
                    using (JsonDocument dDoc = JsonDocument.Parse(detailJson))
                    {
                        if (dDoc.RootElement.TryGetProperty("response", out var dr) && dr.TryGetProperty("order_list", out var dl))
                        {
                            lock (_lock)
                            {
                                foreach (var o in dl.EnumerateArray())
                                {
                                    var ord = new Order { OrderId = o.GetProperty("order_sn").GetString()!, CreatedAt = o.GetProperty("create_time").GetInt64(), Status = 0 };
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
                                            SKU = it.GetProperty("model_sku").GetString() ?? "",
                                            Shelf = itemLocation.ContainsKey("Shelf") ? $"Kệ {itemLocation["Shelf"]}" : null,
                                            Level = itemLocation.ContainsKey("Level") ? $" - Ngăn {itemLocation["Level"]}" : null,
                                            Box = itemLocation.ContainsKey("Box") ? $" - Thùng {itemLocation["Box"]}" : null
                                        });
                                    }
                                    _dbOrders.Add(ord);
                                }
                            }
                        }
                    }
                }
            }
        }

        static async Task StartServer()
        {
            var listener = new HttpListener();
            listener.Prefixes.Add("http://localhost:8080/");
            try
            {
                listener.Start();
                Console.WriteLine("Web: http://localhost:8080/");
                try { Process.Start(new ProcessStartInfo("http://localhost:8080/") { UseShellExecute = true }); } catch { }
            }
            catch (HttpListenerException hlex)
            {
                // Lỗi này thường do chưa chạy quyền Admin
                Console.WriteLine($"[LỖI 8080] Không thể mở Port 8080. Hãy chạy App bằng 'Run as Administrator'.");
                Console.WriteLine($"Chi tiết: {hlex.Message}");
                return; // Thoát hàm StartServer, sẽ rơi xuống finally của Main
            }

            while (true)
            {
                try
                {
                    var ctx = await listener.GetContextAsync(); // Dùng Async để mượt hơn
                    var req = ctx.Request;
                    var resp = ctx.Response;
                    string url = req.Url.AbsolutePath;

                    // ... (Logic Router giữ nguyên: /, /api/data, /api/assign) ...

                    if (url == "/")
                    {
                        byte[] b = Encoding.UTF8.GetBytes(HtmlTemplates.Index);
                        resp.ContentType = "text/html; charset=utf-8"; resp.OutputStream.Write(b, 0, b.Length);
                    }
                    else if (url == "/api/data")
                    {
                        string j; lock (_lock) { j = JsonSerializer.Serialize(_dbOrders); }
                        byte[] b = Encoding.UTF8.GetBytes(j);
                        resp.ContentType = "application/json"; resp.OutputStream.Write(b, 0, b.Length);
                    }
                    else if (url == "/api/ship" && req.HttpMethod == "POST")
                    {
                        string sn = req.QueryString["id"];
                        Console.WriteLine($"[SHIP] Đang xử lý đơn: {sn}");

                        // --- FIX LOGIC CALL API ---
                        try
                        {
                            // 1. Lấy tham số ship (quan trọng)
                            string paramJson = await ShopeeApiHelper.GetShippingParam(sn);
                            Console.WriteLine($"[DEBUG-SHIP] Param: {paramJson}");

                            object shipPayload = null;
                            using (var doc = JsonDocument.Parse(paramJson))
                            {
                                if (doc.RootElement.TryGetProperty("response", out var r))
                                {
                                    // 1. Kiểm tra xem Shopee có trả về danh sách địa chỉ lấy hàng (Pickup) không
                                    // Cấu trúc: response -> pickup -> address_list
                                    if (r.TryGetProperty("pickup", out var pickupData) &&
                                        pickupData.TryGetProperty("address_list", out var addrList) &&
                                        addrList.GetArrayLength() > 0)
                                    {
                                        // Lấy địa chỉ đầu tiên trong danh sách
                                        var firstAddr = addrList[0];
                                        long addrId = firstAddr.GetProperty("address_id").GetInt64();

                                        // Mẫu JSON của bạn yêu cầu cả "pickup_time_id"
                                        // Nó nằm trong: address_list[0] -> time_slot_list[0] -> pickup_time_id
                                        string timeId = "";
                                        if (firstAddr.TryGetProperty("time_slot_list", out var timeList) && timeList.GetArrayLength() > 0)
                                        {
                                            timeId = timeList[0].GetProperty("pickup_time_id").GetString();
                                        }

                                        // Tạo lệnh Ship (Kèm cả ID địa chỉ và ID giờ lấy hàng)
                                        shipPayload = new
                                        {
                                            order_sn = sn,
                                            pickup = new
                                            {
                                                address_id = addrId,
                                                pickup_time_id = timeId
                                            }
                                        };
                                        Console.WriteLine($"-> Mode: PICKUP (Addr: {addrId} | Time: {timeId})");
                                    }
                                    // 2. Nếu không phải Pickup thì là Dropoff (Gửi bưu cục)
                                    else
                                    {
                                        // Dropoff thường không cần tham số gì phức tạp
                                        shipPayload = new { order_sn = sn, dropoff = new { } };
                                        Console.WriteLine("-> Mode: DROPOFF");
                                    }
                                }
                            }

                            if (shipPayload != null)
                            {
                                string shipRes = await ShopeeApiHelper.ShipOrder(shipPayload);
                                Console.WriteLine($"[SHIP KẾT QUẢ] shipRes");
                            }
                            else
                            {
                                Console.WriteLine("[SHIP LỖI] Không xác định được phương thức vận chuyển (Pickup/Dropoff)");
                            }
                        }
                        catch (Exception apiEx)
                        {
                            Console.WriteLine($"[SHIP EXCEPTION] {apiEx.Message}");
                        }
                    }
                    resp.Close();
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[SERVER ERR] {ex.Message}");
                }
            }
        }
    }
}