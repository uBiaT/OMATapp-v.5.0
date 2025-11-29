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

namespace ShopeeConsoleApp
{
    public static class ShopeeApiHelper
    {
        // --- CẤU HÌNH TĨNH (ĐỌC TỪ APPSETTINGS.JSON) ---
        public static long PartnerId { get; private set; }
        public static string PartnerKey { get; private set; } = string.Empty;
        public static string BaseUrl { get; private set; } = string.Empty;
        public static string RegisteredCallbackUri { get; private set; } = string.Empty;
        public static string AppPassword { get; private set; } = "";

        // --- CẤU HÌNH ĐỘNG (ĐỌC TỪ TOKEN.JSON) ---
        public static long SavedShopId { get; private set; }
        public static string? SavedAccessToken { get; private set; }
        public static string? SavedRefreshToken { get; private set; }

        // API Endpoints
        public const string AuthPath = "/api/v2/shop/auth_partner";
        public const string TokenPath = "/api/v2/auth/token/get";
        public const string RefreshPath = "/api/v2/auth/access_token/get";
        public const string ShopInfoPath = "/api/v2/shop/get_shop_info";
        public const string OrderListPath = "/api/v2/order/get_order_list";
        public const string OrderDetailPath = "/api/v2/order/get_order_detail";

        static ShopeeApiHelper() { LoadConfig(); }

        private static void LoadConfig()
        {
            // 1. ĐỌC FILE TĨNH (APPSETTINGS.JSON)
            var builder = new ConfigurationBuilder().SetBasePath(Directory.GetCurrentDirectory()).AddJsonFile("appsettings.json", optional: false, reloadOnChange: true);
            IConfiguration config = builder.Build();
            var section = config.GetSection("ShopeeSettings");

            PartnerId = long.Parse(section["PartnerId"] ?? "0");
            PartnerKey = section["PartnerKey"] ?? string.Empty;
            BaseUrl = section["BaseUrl"] ?? string.Empty;
            RegisteredCallbackUri = section["CallbackUrl"] ?? string.Empty;
            AppPassword = section["AppPassword"] ?? "";

            // 2. ĐỌC FILE ĐỘNG (TOKEN.JSON) - NẾU CÓ
            // File này nằm ở thư mục bin/Debug, không bị VS ghi đè
            string tokenFilePath = Path.Combine(Directory.GetCurrentDirectory(), "token.json");
            if (File.Exists(tokenFilePath))
            {
                try
                {
                    string json = File.ReadAllText(tokenFilePath);
                    using var doc = JsonDocument.Parse(json);
                    var root = doc.RootElement;

                    if (root.TryGetProperty("SavedShopId", out var sid)) SavedShopId = sid.GetInt64();
                    if (root.TryGetProperty("SavedAccessToken", out var acc)) SavedAccessToken = acc.GetString();
                    if (root.TryGetProperty("SavedRefreshToken", out var @ref)) SavedRefreshToken = @ref.GetString();
                }
                catch { /* Bỏ qua nếu file lỗi */ }
            }
        }

        // LƯU TOKEN VÀO FILE TOKEN.JSON RIÊNG BIỆT
        public static void SaveTokenToConfig(long shopId, string? accessToken, string? refreshToken)
        {
            try
            {
                string filePath = Path.Combine(Directory.GetCurrentDirectory(), "token.json");

                var data = new
                {
                    SavedShopId = shopId,
                    SavedAccessToken = accessToken ?? "",
                    SavedRefreshToken = refreshToken ?? "",
                    LastUpdated = DateTime.Now.ToString() // Lưu thêm thời gian để biết
                };

                string jsonString = JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(filePath, jsonString);

                // Cập nhật RAM
                SavedShopId = shopId;
                SavedAccessToken = accessToken;
                SavedRefreshToken = refreshToken;
            }
            catch { }
        }

        public static string GenerateSignature(string path, long timeStamp, string? accessToken = null, long shopId = 0)
        {
            string baseString = $"{PartnerId}{path}{timeStamp}";
            if (!string.IsNullOrEmpty(accessToken) && shopId > 0) baseString += $"{accessToken}{shopId}";
            var key = Encoding.UTF8.GetBytes(PartnerKey);
            using (var hmac = new HMACSHA256(key)) return BitConverter.ToString(hmac.ComputeHash(Encoding.UTF8.GetBytes(baseString))).Replace("-", "").ToLowerInvariant();
        }

        public static async Task<string> SendApiRequest(string apiPath, string? accessToken, long shopId, Dictionary<string, string?>? specificParams = null)
        {
            long timeStamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            string signature = GenerateSignature(apiPath, timeStamp, accessToken, shopId);
            var queryParams = new Dictionary<string, string?> { { "partner_id", PartnerId.ToString() }, { "timestamp", timeStamp.ToString() }, { "access_token", accessToken }, { "shop_id", shopId.ToString() }, { "sign", signature } };
            if (specificParams != null) foreach (var param in specificParams) queryParams.Add(param.Key, param.Value);
            string fullUrl = QueryHelpers.AddQueryString($"{BaseUrl}{apiPath}", queryParams);
            using (var client = new HttpClient())
            {
                try { return await (await client.GetAsync(fullUrl)).Content.ReadAsStringAsync(); }
                catch (Exception ex) { return JsonSerializer.Serialize(new { error = "http_error", message = ex.Message }); }
            }
        }

        private static async Task<string> PostAuthRequest(string path, object body, long timeStamp)
        {
            string jsonBody = JsonSerializer.Serialize(body, new JsonSerializerOptions { WriteIndented = false });
            string signature = GenerateSignature(path, timeStamp);
            string fullUrl = $"{BaseUrl}{path}?partner_id={PartnerId}&timestamp={timeStamp}&sign={signature}";
            using (var client = new HttpClient()) return await (await client.PostAsync(fullUrl, new StringContent(jsonBody, Encoding.UTF8, "application/json"))).Content.ReadAsStringAsync();
        }

        public static async Task<string> GetShopInfo(string? accessToken, long shopId) => await SendApiRequest(ShopInfoPath, accessToken, shopId);

        public static async Task<string> GetOrderList(string? accessToken, long shopId)
        {
            long fromDate = DateTimeOffset.UtcNow.AddDays(-14).ToUnixTimeSeconds();
            var parameters = new Dictionary<string, string?> { { "time_range_field", "create_time" }, { "time_from", fromDate.ToString() }, { "time_to", DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString() }, { "page_size", "50" }, { "order_status", "READY_TO_SHIP" } };
            return await SendApiRequest(OrderListPath, accessToken, shopId, parameters);
        }

        public static async Task<string> GetOrderDetail(string? accessToken, long shopId, string orderSnList)
        {
            var parameters = new Dictionary<string, string?> { { "order_sn_list", orderSnList }, { "response_optional_fields", "item_list" } };
            return await SendApiRequest(OrderDetailPath, accessToken, shopId, parameters);
        }

        public static async Task<string> ExchangeCodeForToken(long receivedShopId, string receivedCode)
        {
            long timeStamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            var body = new { partner_id = PartnerId, shop_id = receivedShopId, code = receivedCode };
            return await PostAuthRequest(TokenPath, body, timeStamp);
        }

        public static async Task<string> RefreshAccessToken(long shopId, string refreshToken)
        {
            long timeStamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            var body = new { partner_id = PartnerId, shop_id = shopId, refresh_token = refreshToken };
            return await PostAuthRequest(RefreshPath, body, timeStamp);
        }
    }
}