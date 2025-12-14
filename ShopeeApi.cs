using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Configuration;

namespace ShopeeServer
{
    public static class ShopeeApiHelper
    {
        // === CẤU HÌNH ===
        public static long PartnerId { get; private set; }
        public static string PartnerKey { get; private set; } = "";
        public static string BaseUrl { get; private set; } = "";
        public static string CallbackUrl { get; private set; } = "";

        // === DỮ LIỆU ĐỘNG ===
        public static long ShopId { get; private set; }
        public static string AccessToken { get; private set; } = "";
        public static string RefreshToken { get; private set; } = "";
        //public static string AppPassword { get; private set; } = "";

        static ShopeeApiHelper() { LoadConfig(); }

        private static void LoadConfig()
        {
            try
            {
                var builder = new ConfigurationBuilder()
                    .SetBasePath(Directory.GetCurrentDirectory())
                    .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true);

                var config = builder.Build().GetSection("ShopeeSettings");

                PartnerId = long.Parse(config["PartnerId"] ?? "0");
                PartnerKey = config["PartnerKey"] ?? "";
                BaseUrl = config["BaseUrl"] ?? "https://partner.shopeemobile.com";
                CallbackUrl = config["CallbackUrl"] ?? "";
                //AppPassword = config["AppPassword"] ?? "";

                long.TryParse(config["SavedShopId"], out long sid); ShopId = sid;
                AccessToken = config["SavedAccessToken"] ?? "";
                RefreshToken = config["SavedRefreshToken"] ?? "";
            }
            catch { }
        }

        public static void SaveTokenToConfig(long shopId, string accessToken, string refreshToken)
        {
            try
            {
                string filePath = Path.Combine(Directory.GetCurrentDirectory(), "appsettings.json");
                // Tạo file mới nếu chưa có
                if (!File.Exists(filePath))
                {
                    var defaultJson = new { ShopeeSettings = new { PartnerId = PartnerId, PartnerKey = PartnerKey, BaseUrl = BaseUrl, CallbackUrl = CallbackUrl, SavedShopId = 0, SavedAccessToken = "", SavedRefreshToken = "" } };
                    File.WriteAllText(filePath, JsonSerializer.Serialize(defaultJson));
                }

                string jsonString = File.ReadAllText(filePath);
                var jsonNode = JsonNode.Parse(jsonString);

                if (jsonNode != null)
                {
                    jsonNode["ShopeeSettings"]!["SavedShopId"] = shopId;
                    jsonNode["ShopeeSettings"]!["SavedAccessToken"] = accessToken;
                    jsonNode["ShopeeSettings"]!["SavedRefreshToken"] = refreshToken;

                    var options = new JsonSerializerOptions { WriteIndented = true };
                    File.WriteAllText(filePath, jsonNode.ToJsonString(options));

                    ShopId = shopId; AccessToken = accessToken; RefreshToken = refreshToken;
                    Program.Log("[Config] Đã lưu Token mới.");
                }
            }
            catch (Exception ex)
            {
                Program.Log($"[Save Error] {ex.Message}");
                SaveTokenToConfig(ShopId, "","");
            }
        }

        public static string GenerateSignature(string path, long timeStamp, bool isAuth = false)
        {
            string baseString = $"{PartnerId}{path}{timeStamp}";
            if (!isAuth) baseString += $"{AccessToken}{ShopId}";
            using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(PartnerKey));
            return BitConverter.ToString(hmac.ComputeHash(Encoding.UTF8.GetBytes(baseString))).Replace("-", "").ToLowerInvariant();
        }

        public static string GetAuthUrl()
        {
            long ts = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            string sign = GenerateSignature("/api/v2/shop/auth_partner", ts, true);
            return $"{BaseUrl}/api/v2/shop/auth_partner?partner_id={PartnerId}&timestamp={ts}&sign={sign}&redirect={CallbackUrl}";
        }

        public static async Task<bool> ExchangeCodeForToken(long shopId, string code)
        {
            return await PostAuth("/api/v2/auth/token/get", new { partner_id = PartnerId, shop_id = shopId, code = code });
        }

        public static async Task<bool> RefreshTokenNow()
        {
            Program.Log("[Auto-Refresh] Đang gia hạn Token...");
            return await PostAuth("/api/v2/auth/access_token/get", new { partner_id = PartnerId, shop_id = ShopId, refresh_token = RefreshToken });
        }

        private static async Task<bool> PostAuth(string path, object body)
        {
            try
            {
                long ts = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                string sign = GenerateSignature(path, ts, true);
                using var client = new HttpClient();
                var resp = await client.PostAsync($"{BaseUrl}{path}?partner_id={PartnerId}&timestamp={ts}&sign={sign}",
                    new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json"));

                var json = await resp.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("access_token", out var t))
                {
                    long sid = ShopId;
                    if (doc.RootElement.TryGetProperty("shop_id", out var s)) sid = s.GetInt64();
                    SaveTokenToConfig(sid, t.GetString()!, doc.RootElement.GetProperty("refresh_token").GetString()!);
                    return true;
                }
                Program.Log($"[Auth Fail] {json}");
            }
            catch (Exception ex) { Program.Log($"[Auth Ex] {ex.Message}"); }
            return false;
        }

        public static async Task<string> CallGetAPI(string path, Dictionary<string, string> p)
        {
            if (string.IsNullOrEmpty(AccessToken)) return "{\"error\":\"no_token\"}";

            long ts = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            string sign = GenerateSignature(path, ts);

            var q = new Dictionary<string, string?> {
                { "partner_id", PartnerId.ToString() },
                { "timestamp", ts.ToString() },
                { "access_token", AccessToken },
                { "shop_id", ShopId.ToString() },
                { "sign", sign }
            };
            foreach (var k in p) q[k.Key] = k.Value;

            // Tạo URL đầy đủ để debug nếu cần
            string requestUrl = QueryHelpers.AddQueryString(BaseUrl + path, q);

            // In ra Console (màu xám hoặc vàng) để dễ nhìn
            Program.Log($"[API-GET] {path}");
            Program.Log(requestUrl);

            using var client = new HttpClient();

            try
            {
                // 2. Dùng GetAsync trước để kiểm tra Status Code
                var response = await client.GetAsync(requestUrl);

                // Nếu Shopee trả về lỗi (403, 400, 500...), ném lỗi ra catch
                response.EnsureSuccessStatusCode();

                //Program.Log(await response.Content.ReadAsStringAsync());

                return await response.Content.ReadAsStringAsync();
            }
            catch (HttpRequestException httpEx)
            {
                // In lỗi cụ thể: Ví dụ "403 Forbidden" hay "404 Not Found"
                Program.Log($"[HTTP ERR] {httpEx.StatusCode} - {httpEx.Message}");
                return "{\"error\":\"network_error\", \"detail\":\"" + httpEx.Message + "\"}";
            }
            catch (Exception ex)
            {
                Program.Log($"[API ERR] {ex.Message}");
                return "{\"error\":\"network_error\"}";
            }
        }
        public static async Task<string> CallPostAPI(string path, object body)
        {
            if (string.IsNullOrEmpty(AccessToken)) return "{\"error\":\"no_token\"}";
            long ts = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            string sign = GenerateSignature(path, ts);
            string url = $"{BaseUrl}{path}?partner_id={PartnerId}&timestamp={ts}&access_token={AccessToken}&shop_id={ShopId}&sign={sign}";

            using var client = new HttpClient();
            var content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
            string jsonBody = JsonSerializer.Serialize(body);
            try
            {
                var resp = await client.PostAsync(url, content);
                Program.Log($"[API-POST] {path}");
                Program.Log($"[API-POST] {url}");
                //Program.Log($"[API-POST] {resp.Content.ReadAsStringAsync()}");
                return await resp.Content.ReadAsStringAsync();
            }
            catch { return "{\"error\":\"network_error\"}"; }
        }
        public static async Task<byte[]> Download(string path, object body)
        {
            long ts = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            string url = $"{BaseUrl}{path}?partner_id={PartnerId}&timestamp={ts}&access_token={AccessToken}&shop_id={ShopId}&sign={GenerateSignature(path, ts)}";

            using var client = new HttpClient();
            var content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
            try
            {
                var resp = await client.PostAsync(url, content);
                if (resp.IsSuccessStatusCode) return await resp.Content.ReadAsByteArrayAsync();
            }
            catch { }
            return Array.Empty<byte>();
        }

        // --- API WRAPPERS ---
        // Trong file ShopeeApi.cs

        // Sửa hàm GetOrderList thành như sau:
        public static async Task<string> GetOrderList(long fromDate, long toDate, string status = "READY_TO_SHIP", string cursor = "")
        {
            var p = new Dictionary<string, string> {
        { "time_range_field", "update_time" },
        { "time_from", fromDate.ToString() },
        { "time_to", toDate.ToString() },
        { "page_size", "100" },
        { "order_status", status }, // <--- Dùng tham số truyền vào
        { "request_order_status_pending", "true" },
        { "cursor", cursor }
    };
            return await CallGetAPI("/api/v2/order/get_order_list", p);
        }

        public static async Task<string> GetOrderDetails(string sns)
        {
            return await CallGetAPI("/api/v2/order/get_order_detail", new Dictionary<string, string> {
                { "order_sn_list", sns }, { "request_order_status_pending", "true" }, { "response_optional_fields", "item_list,total_amount,shipping_carrier" }
            });
        }

        public static async Task<string> GetItemModelList(long itemId)
        {
            return await CallGetAPI("/api/v2/product/get_model_list", new Dictionary<string, string> {
                { "item_id", itemId.ToString() }
            });
        }

        // --- CÁC API LOGISTICS CỤ THỂ ---

        // B1: Lấy tham số vận chuyển (Pickup hay Dropoff?)
        public static async Task<string> GetShippingParam(string sn) =>
            await CallGetAPI("/api/v2/logistics/get_shipping_parameter", new Dictionary<string, string> { { "order_sn", sn } });

        // B2: Báo chuẩn bị hàng
        public static async Task<string> ShipOrder(object payload) =>
            await CallPostAPI("/api/v2/logistics/ship_order", payload);

        public static async Task<string> GetBatchDocResult(List<string> orderSns)
        {
            if (orderSns == null || orderSns.Count == 0) return "{}";

            // Tạo danh sách object theo cấu trúc API yêu cầu
            var orderListPayload = orderSns.Select(sn => new { order_sn = sn }).ToArray();

            var payload = new
            {
                order_list = orderListPayload,
                shipping_document_type = "THERMAL_AIR_WAYBILL"
            };

            return await CallPostAPI("/api/v2/logistics/get_shipping_document_result", payload);
        }

        // B3: Tạo yêu cầu in (In Nhiệt)
        public static async Task<string> CreateDoc(string sn) =>
            await CallPostAPI("/api/v2/logistics/create_shipping_document", new { order_list = new[] { new { order_sn = sn } }, shipping_document_type = "THERMAL_AIR_WAYBILL" });

        // B4: Kiểm tra trạng thái tạo file
        public static async Task<string> GetDocResult(string sn) =>
            await CallPostAPI("/api/v2/logistics/get_shipping_document_result", new { order_list = new[] { new { order_sn = sn } } });

        // B5: Tải file PDF
        public static async Task<byte[]> DownloadDoc(string sn) =>
            await Download("/api/v2/logistics/download_shipping_document", new { order_list = new[] { new { order_sn = sn } }, shipping_document_type = "THERMAL_AIR_WAYBILL" });
    }
}