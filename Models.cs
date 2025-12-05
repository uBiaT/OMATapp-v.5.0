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

        // UI Helper (Không lưu DB)
        public bool Selected { get; set; } = false;
        public string Note { get; set; } = ""; // Ghi chú đơn hàng
    }

    public class OrderItem
    {
        public long ItemId { get; set; }
        public string ModelName { get; set; } = ""; // Tên phân loại
        public string ProductName { get; set; } = ""; // Tên sản phẩm
        public string ImageUrl { get; set; } = "";
        public int Quantity { get; set; }
        public int Price { get; set; }
        public string SKU { get; set; } = "";
        public string? Shelf { get; set; }
        public string? Level { get; set; }
        public string? Box { get; set; }


        // Thuộc tính dùng cho giao diện Batch Picking
        public bool Picked { get; set; } = false;
        public List<string> OrderIds { get; set; } = new List<string>();
        public int TotalQty { get; set; }
        public bool ShowDetail { get; set; } = false;
    }
}