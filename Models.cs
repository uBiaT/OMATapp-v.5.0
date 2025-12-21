using System.Collections.Generic;

namespace ShopeeServer
{
    public class Order
    {
        public string OrderId { get; set; } = ""; // Mã đơn hàng (Order SN)
        public int Status { get; set; } = 0; // 0: Mới, 1: Đã in/Xử lý
        public long UpdateAt { get; set; } // Thời gian đặt hàng
        public List<OrderItem> Items { get; set; } = [];
        public decimal TotalAmount { get; set; } = 0; // Tổng tiền đơn
        public decimal TotalItems { get; set; } = 0; // Tổng số món
        public string ShippingCarrier { get; set; } = ""; // Đơn vị vận chuyển (Ví dụ: SPX Express, J&T...)
        public string TrackingNumber { get; set; } = "";
        public bool Printed { get; set; } = false; // True khi API trả về status = READY
        public string Picker { get; set; } = "";       // Người soạn
        public string PickingStatus { get; set; } = "";// Trạng thái soạn

        // UI Helper (Không lưu DB)
        public bool Selected { get; set; } = false;
        public string Note { get; set; } = ""; // Ghi chú đơn hàng
    }

    public class OrderItem
    {
        public long ProductId { get; set; }
        public string ProductName { get; set; } = ""; // Tên sản phẩm
        public long ModelId { get; set; }
        public string ModelName { get; set; } = ""; // Tên phân loại
        public string ImageUrl { get; set; } = "";
        public int Quantity { get; set; }
        public string? Shelf { get; set; }
        public string? Level { get; set; }
        public string? Box { get; set; }
        public decimal Price { get; set; } = 0; // Giá bán của món này


        // Thuộc tính dùng cho giao diện Batch Picking
        public bool Picked { get; set; } = false;
        public List<string> OrderIds { get; set; } = [];
        public int TotalQty { get; set; }
        public bool ShowDetail { get; set; } = false;
    }
    public class BatchUpdateReq
    {
        public List<string> Ids { get; set; } = [];
        public string? Field { get; set; } // "picker" hoặc "status"
        public string? Value { get; set; }
    }
}