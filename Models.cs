using System.Collections.Generic;

namespace ShopeeServer
{
    public class Order
    {
        public string OrderId { get; set; } = ""; // Mã đơn hàng (Order SN)
        public int Status { get; set; } = 0; // 0: Mới, 1: Đã in/Xử lý
        public string AssignedTo { get; set; } = ""; // Tên nhân viên
        public long CreatedAt { get; set; } // Thời gian đặt hàng
        public List<OrderItem> Items { get; set; } = new List<OrderItem>();
        public decimal TotalAmount { get; set; } = 0; // Tổng tiền đơn
        public decimal TotalItems { get; set; } = 0; // Tổng số món
        public string ShippingCarrier { get; set; } = ""; // Đơn vị vận chuyển (Ví dụ: SPX Express, J&T...)
        public string TrackingNumber { get; set; } = "";
        public bool Printed { get; set; } = false; // True khi API trả về status = READY

        // UI Helper (Không lưu DB)
        public bool Selected { get; set; } = false;
        public string Note { get; set; } = ""; // Ghi chú đơn hàng
    }

    public class OrderItem
    {
        public long ItemId { get; set; }
        public string ProductName { get; set; } = ""; // Tên sản phẩm
        public string ModelName { get; set; } = ""; // Tên phân loại
        public string ImageUrl { get; set; } = "";
        public int Quantity { get; set; }
        public string? Shelf { get; set; }
        public string? Level { get; set; }
        public string? Box { get; set; }
        public decimal Price { get; set; } = 0; // Giá bán của món này


        // Thuộc tính dùng cho giao diện Batch Picking
        public bool Picked { get; set; } = false;
        public List<string> OrderIds { get; set; } = new List<string>();
        public int TotalQty { get; set; }
        public bool ShowDetail { get; set; } = false;
    }
}