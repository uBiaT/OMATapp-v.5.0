using Microsoft.AspNetCore.WebUtilities;
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

namespace ShopeeServer
{
    class Program
    {
        private static List<Order> _dbOrders = new List<Order>();
        private static object _lock = new object();

        static async Task Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            Console.WriteLine("=== SHOPEE LIVE SERVER ===");

            // GIAI ĐOẠN 0: CHECK AUTH
            if (string.IsNullOrEmpty(ShopeeApiHelper.AccessToken))
            {
                Console.WriteLine("[WARN] Chưa có Token. Vui lòng copy link này để đăng nhập:");
                Console.WriteLine(ShopeeApiHelper.GetAuthUrl());
                Console.Write("Callback URL: ");
                string cb = Console.ReadLine()?.Trim() ?? "";
                if (!string.IsNullOrEmpty(cb))
                {
                    try
                    {
                        var q = QueryHelpers.ParseQuery(new Uri(cb).Query);
                        long sid = long.Parse(q["shop_id"]!);
                        string code = q["code"]!;
                        if (await ShopeeApiHelper.ExchangeCodeForToken(sid, code)) Console.WriteLine("[OK] Đã lưu Token.");
                        else return;
                    }
                    catch { Console.WriteLine("URL sai."); return; }
                }
                else return;
            }

            // GIAI ĐOẠN 1: START SYNC
            _ = Task.Run(async () => {
                while (true)
                {
                    try { await CoreEngineSync(); } catch (Exception ex) { Console.WriteLine($"[SyncErr] {ex.Message}"); }
                    await Task.Delay(TimeSpan.FromMinutes(1));
                }
            });

            // GIAI ĐOẠN 2: START SERVER
            StartServer();
        }

        static async Task CoreEngineSync()
        {
            Console.WriteLine($"[{DateTime.Now:HH:mm}] Quét đơn...");
            long to = DateTimeOffset.UtcNow.ToUnixTimeSeconds(), from = DateTimeOffset.UtcNow.AddDays(-15).ToUnixTimeSeconds();

            // 1. GỌI API DANH SÁCH
            string json = await ShopeeApiHelper.GetOrderList(from, to);

            // [LOGIC MỚI] KIỂM TRA LỖI TOKEN
            // Nếu Shopee trả về lỗi "error_auth" hoặc "error_token", nghĩa là token hết hạn
            if (json.Contains("\"error\":\"invalid_acceess_token\""))
            {
                Console.WriteLine($"[Token Expired] Phát hiện lỗi Auth: {json}");
                // Thử refresh token
                bool refreshed = await ShopeeApiHelper.RefreshTokenNow();
                if (refreshed)
                {
                    // Nếu refresh thành công, gọi lại API lần nữa
                    Console.WriteLine("[Retry] Đang gọi lại API sau khi Refresh...");
                    json = await ShopeeApiHelper.GetOrderList(from, to);
                }
                else
                {
                    Console.WriteLine("[STOP] Refresh thất bại. Vui lòng đăng nhập lại thủ công.");
                    return;
                }
            }

            List<string> liveIds = new List<string>();
            using (JsonDocument doc = JsonDocument.Parse(json))
            {
                // Kiểm tra kỹ lại lần nữa
                if (doc.RootElement.TryGetProperty("\"error\":\"\"", out var err))
                {
                    Console.WriteLine($"[API Skip] Lỗi API: {json}"); return;
                }

                if (doc.RootElement.TryGetProperty("response", out var r) && r.TryGetProperty("order_list", out var l))
                    foreach (var i in l.EnumerateArray()) liveIds.Add(i.GetProperty("order_sn").GetString()!);
            }

            lock (_lock) { _dbOrders.RemoveAll(o => o.Status == 0 && !liveIds.Contains(o.OrderId)); }

            List<string> newIds;
            lock (_lock) { newIds = liveIds.Where(id => !_dbOrders.Any(o => o.OrderId == id)).ToList(); }

            if (newIds.Count > 0)
            {
                Console.WriteLine($"[Sync] Phát hiện {newIds.Count} đơn mới.");
                for (int i = 0; i < newIds.Count; i += 50)
                {
                    string snStr = string.Join(",", newIds.Skip(i).Take(50));

                    // Gọi API Chi tiết (Cũng có thể lỗi token ở đây, nhưng thường thì List OK thì Detail cũng OK)
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
                                        string loc = "Kho";
                                        var m = Regex.Match(name, @"\[(.*?)\]");
                                        if (m.Success) loc = m.Groups[1].Value;
                                        ord.Items.Add(new OrderItem
                                        {
                                            ItemId = it.GetProperty("item_id").GetInt64(),
                                            ProductName = it.GetProperty("item_name").GetString()!,
                                            ModelName = name,
                                            ImageUrl = it.GetProperty("image_info").GetProperty("image_url").GetString()!,
                                            Quantity = it.GetProperty("model_quantity_purchased").GetInt32(),
                                            SKU = it.GetProperty("model_sku").GetString() ?? "",
                                            Location = loc
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

        static void StartServer()
        {
            var listener = new HttpListener();
            listener.Prefixes.Add("http://+:8080/");
            listener.Start();
            Console.WriteLine("Web: http://localhost:8080");
            Process.Start(new ProcessStartInfo("http://localhost:8080") { UseShellExecute = true });

            while (true)
            {
                try
                {
                    var ctx = listener.GetContext();
                    var req = ctx.Request;
                    var resp = ctx.Response;
                    string url = req.Url.AbsolutePath;

                    if (url == "/")
                    {
                        byte[] b = Encoding.UTF8.GetBytes(HtmlTemplates.Index);
                        resp.ContentType = "text/html"; resp.OutputStream.Write(b, 0, b.Length);
                    }
                    else if (url == "/api/data")
                    {
                        string j; lock (_lock) { j = JsonSerializer.Serialize(_dbOrders); }
                        byte[] b = Encoding.UTF8.GetBytes(j);
                        resp.ContentType = "application/json"; resp.OutputStream.Write(b, 0, b.Length);
                    }
                    else if (url == "/api/assign")
                    {
                        string id = req.QueryString["id"], u = req.QueryString["user"];
                        lock (_lock) { var o = _dbOrders.FirstOrDefault(x => x.OrderId == id); if (o != null) o.AssignedTo = u; }
                    }
                    else if (url == "/api/ship")
                    {
                        string id = req.QueryString["id"];
                        lock (_lock) { var o = _dbOrders.FirstOrDefault(x => x.OrderId == id); if (o != null) o.Status = 1; }
                    }
                    resp.Close();
                }
                catch { }
            }
        }
    }
}