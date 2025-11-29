namespace ShopeeServer
{
    public static class HtmlTemplates
    {
        public static string Index = @"
<!DOCTYPE html>
<html lang='vi'>
<head>
    <meta charset='UTF-8'>
    <meta name='viewport' content='width=device-width, initial-scale=1, maximum-scale=1, user-scalable=no'>
    <title>Shopee WMS Final</title>
    <link href='https://cdn.jsdelivr.net/npm/bootstrap@5.3.0/dist/css/bootstrap.min.css' rel='stylesheet'>
    <link rel='stylesheet' href='https://cdn.jsdelivr.net/npm/bootstrap-icons@1.11.0/font/bootstrap-icons.css'>
    <script src='https://unpkg.com/vue@3/dist/vue.global.js'></script>
    <style>
        body { background-color: #f0f2f5; padding-bottom: 100px; font-size: 14px; font-family: -apple-system, sans-serif; }
        
        /* === 1. CẤU TRÚC THẺ SẢN PHẨM (CARD ITEM) === */
        /* Dùng chung cho cả Quản lý đơn & Lộ trình để đồng nhất */
        .card-item { background: white; border-radius: 8px; box-shadow: 0 1px 2px rgba(0,0,0,0.05); margin-bottom: 8px; border: 1px solid #eee; overflow: hidden; }
        
        .card-layout { display: flex; padding: 10px; align-items: flex-start; position: relative; }
        
        /* Cột 1: Ảnh */
        .col-img { width: 70px; height: 70px; flex-shrink: 0; margin-right: 12px; position: relative; }
        .img-thumb { width: 100%; height: 100%; object-fit: cover; border-radius: 6px; border: 1px solid #eee; }
        .btn-zoom { position: absolute; bottom: 0; right: 0; background: rgba(0,0,0,0.6); color: white; font-size: 10px; padding: 2px 5px; border-radius: 4px 0 4px 0; }

        /* Cột 2: Thông tin (Tên - Phân loại - Vị trí) */
        .col-info { flex-grow: 1; min-width: 0; display: flex; flex-direction: column; cursor: pointer; }
        .text-name { font-weight: 700; color: #333; margin-bottom: 3px; line-height: 1.3; font-size: 13px; display: -webkit-box; -webkit-line-clamp: 2; -webkit-box-orient: vertical; overflow: hidden; }
        .badge-var { font-size: 12px; color: #e65100; background: #fff3e0; border: 1px solid #ffe0b2; padding: 1px 6px; border-radius: 4px; width: fit-content; margin-bottom: 3px; font-weight: 500; }
        .badge-loc { font-size: 11px; color: #1565c0; background: #e3f2fd; padding: 1px 6px; border-radius: 4px; width: fit-content; font-weight: bold; border: 1px solid #bbdefb; }
        
        /* Cột 3: Hành động (Số lượng / Checkbox) */
        .col-action { text-align: right; padding-left: 8px; display: flex; flex-direction: column; align-items: flex-end; min-width: 40px; }
        .text-qty { font-size: 16px; font-weight: 800; color: #333; line-height: 1; margin-bottom: 8px; }
        .text-qty.red { color: #d32f2f; font-size: 18px; }
        .chk-done { width: 24px; height: 24px; cursor: pointer; accent-color: #2e7d32; }

        /* === 2. MÀN HÌNH QUẢN LÝ (ACCORDION) === */
        .order-row { padding: 12px; background: white; border-bottom: 1px solid #f0f0f0; cursor: pointer; display: flex; justify-content: space-between; align-items: center; border-radius: 8px; margin-bottom: 8px; box-shadow: 0 1px 2px rgba(0,0,0,0.05); }
        .order-row.active { background: #e3f2fd; border: 1px solid #90caf9; margin-bottom: 0; border-radius: 8px 8px 0 0; position: sticky; top: 60px; z-index: 90; }
        
        .order-detail-box { background: #fafafa; border: 1px solid #90caf9; border-top: none; border-radius: 0 0 8px 8px; padding: 10px; margin-bottom: 10px; animation: slideDown 0.2s ease-out; }
        
        /* Highlight 4 số cuối mã đơn */
        .sn-wrapper { color: #555; font-family: monospace; font-size: 1.1em; }
        .hl-sn { color: #d32f2f; font-weight: 900; font-size: 1.2em; letter-spacing: 0.5px; }

        /* === 3. MÀN HÌNH LỘ TRÌNH (PICKING) === */
        .group-header { background: #ff9800; color: white; padding: 8px 12px; font-weight: bold; border-radius: 6px; margin-top: 15px; margin-bottom: 8px; font-size: 13px; display: flex; align-items: center; box-shadow: 0 2px 4px rgba(255, 152, 0, 0.3); }
        
        .card-item.is-picked { opacity: 0.5; background: #f8f9fa; filter: grayscale(100%); }
        .card-item.is-picked .text-name { text-decoration: line-through; }

        /* Dropdown danh sách đơn con */
        .dropdown-list { background: #fff8e1; padding: 10px; border-top: 1px dashed #fb8c00; display: none; }
        .dropdown-list.open { display: block; animation: fadeIn 0.2s; }
        .tag-sn { font-family: monospace; font-size: 12px; background: white; border: 1px solid #ffd54f; padding: 3px 8px; border-radius: 4px; margin-right: 5px; margin-bottom: 5px; display: inline-block; color: #333; }

        /* === 4. MODAL CHI TIẾT SẢN PHẨM === */
        .modal-img-main { width: 100%; height: 250px; object-fit: contain; border-radius: 8px; background: #fff; border: 1px solid #eee; }
        .comp-item { display: flex; align-items: center; padding: 8px 0; border-bottom: 1px solid #f0f0f0; }
        .comp-item.active { background-color: #e3f2fd; border-radius: 6px; padding: 8px; border: 1px solid #90caf9; }
        .comp-img { width: 40px; height: 40px; object-fit: cover; border-radius: 4px; border: 1px solid #ddd; margin-right: 10px; }
        .comp-stock { font-weight: bold; color: #198754; font-size: 14px; white-space: nowrap; }
        .comp-stock.low { color: #d32f2f; }

        /* === UTILS === */
        .btn-float { position: fixed; bottom: 20px; left: 50%; transform: translateX(-50%); width: 90%; max-width: 500px; padding: 14px; border-radius: 50px; font-weight: bold; box-shadow: 0 4px 15px rgba(0,0,0,0.3); z-index: 1000; font-size: 16px; border: none; }
        .btn-print { background: #d32f2f; color: white; width: 100%; padding: 10px; border-radius: 6px; border: none; font-weight: bold; margin-top: 5px; }
        
        @keyframes slideDown { from { opacity: 0; transform: translateY(-10px); } to { opacity: 1; transform: translateY(0); } }
        @keyframes fadeIn { from { opacity: 0; } to { opacity: 1; } }
    </style>
</head>
<body>
<div id='app' class='container py-2' style='max-width: 600px'>
    
    <div class='d-flex justify-content-between align-items-center mb-3 bg-white p-3 rounded shadow-sm sticky-top'>
        <span class='fw-bold text-primary h5 mb-0'><i class='bi bi-box-seam-fill'></i> KHO HÀNG</span>
        <div class='d-flex gap-2'>
            <button class='btn btn-sm btn-outline-primary' @click='fetchData'><i class='bi bi-arrow-clockwise'></i></button>
        </div>
    </div>

    <div v-if='currentView === ""manager""'>
        <ul class='nav nav-pills nav-fill mb-3 bg-white p-1 rounded shadow-sm'>
            <li class='nav-item'><a class='nav-link' :class='{active: tab===""unprocessed""}' @click='tab=""unprocessed""'>Chờ xử lý ({{unprocessedOrders.length}})</a></li>
            <li class='nav-item'><a class='nav-link' :class='{active: tab===""processed""}' @click='tab=""processed""'>Đã xử lý ({{processedOrders.length}})</a></li>
        </ul>

        <div class='d-flex justify-content-between mb-3 align-items-center' v-if='tab===""unprocessed""'>
            <button class='btn btn-sm btn-white border shadow-sm' @click='sortDesc = !sortDesc'>
                <i class='bi' :class='sortDesc ? ""bi-sort-down"" : ""bi-sort-up""'></i> {{sortDesc ? 'Mới nhất' : 'Cũ nhất'}}
            </button>
            <button class='btn btn-sm fw-bold shadow-sm' :class='isBatchMode ? ""btn-danger"" : ""btn-warning""' @click='toggleBatchMode'>
                {{ isBatchMode ? '❌ Hủy chọn' : '📦 Gom đơn' }}
            </button>
        </div>

        <div v-for='order in filteredOrders' :key='order.OrderId'>
            <div class='order-row' :class='{active: openOrderId === order.OrderId}' @click='toggleOrder(order.OrderId)'>
                <div class='d-flex align-items-center'>
                    <input v-if='isBatchMode' type='checkbox' class='form-check-input me-3' style='width:22px;height:22px' v-model='order.Selected' @click.stop>
                    <span class='sn-wrapper'>
                        {{order.OrderId.slice(0, -4)}}<span class='hl-sn'>{{order.OrderId.slice(-4)}}</span>
                    </span>
                </div>
                <div class='d-flex align-items-center gap-2'>
                    <span class='badge bg-secondary rounded-pill'>{{order.Items.length}} loại</span>
                    <i class='bi' :class='openOrderId === order.OrderId ? ""bi-chevron-up"" : ""bi-chevron-down""'></i>
                </div>
            </div>

            <div v-if='openOrderId === order.OrderId' class='order-detail-box'>
                <div v-for='item in order.Items' class='card-item border-0 mb-2'>
                    <div class='card-layout p-0 pb-2 border-bottom mb-2'>
                        <div class='col-img' @click.stop='showProductModal(item)'>
                            <img :src='item.ImageUrl' class='img-thumb'>
                            <div class='btn-zoom'><i class='bi bi-eye-fill'></i></div>
                        </div>
                        <div class='col-info'>
                            <div class='text-name'>{{item.ProductName}}</div>
                            <div class='badge-var'>{{item.ModelName}}</div>
                            <div class='badge-loc'><i class='bi bi-geo-alt-fill'></i> {{item.ParsedLocation}}</div>
                        </div>
                        <div class='col-action'>
                            <span class='text-qty' :class='{red: item.Quantity > 1}'>x{{item.Quantity}}</span>
                        </div>
                    </div>
                </div>
                <div class='mt-2' v-if='!isBatchMode && order.Status === 0'>
                    <button class='btn-print' @click='shipOrder(order.OrderId)'><i class='bi bi-printer-fill'></i> CHUẨN BỊ & IN</button>
                </div>
            </div>
        </div>

        <button v-if='isBatchMode && selectedCount > 0' class='btn btn-warning btn-float text-white' @click='startPicking'>
            BẮT ĐẦU SOẠN ({{selectedCount}}) <i class='bi bi-arrow-right'></i>
        </button>
    </div>

    <div v-if='currentView === ""picking""'>
        <div class='sticky-top bg-white p-3 shadow-sm d-flex justify-content-between align-items-center mb-3'>
            <button class='btn btn-outline-secondary btn-sm' @click='currentView=""manager""'>Thoát</button>
            <span class='fw-bold text-warning'>LỘ TRÌNH ĐI NHẶT</span>
            <span class='badge bg-warning text-dark'>{{batchItems.length}} dòng</span>
        </div>

        <div v-for='(group, loc) in groupedBatch' :key='loc'>
            <div class='group-header'><i class='bi bi-geo-alt-fill me-1'></i> {{loc}}</div>
            
            <div v-for='item in group' class='card-item' :class='{""is-picked"": item.Picked}'>
                <div class='card-layout'>
                    <div class='col-img' @click.stop='showProductModal(item)'>
                        <img :src='item.ImageUrl' class='img-thumb'>
                        <div class='btn-zoom'><i class='bi bi-eye-fill'></i></div>
                    </div>
                    
                    <div class='col-info' @click='item.ShowDetail = !item.ShowDetail'>
                        <div class='text-name'>{{item.ProductName}}</div>
                        <div class='badge-var'>{{item.ModelName}}</div>
                        
                        <div class='hint-expand' v-if='!item.ShowDetail'>
                            <span class='text-muted small'><i class='bi bi-caret-down-fill'></i> Xem {{item.OrderIds.length}} đơn</span>
                        </div>
                         <div class='hint-expand' v-else>
                            <span class='text-muted small'><i class='bi bi-caret-up-fill'></i> Thu gọn</span>
                        </div>
                    </div>

                    <div class='col-action'>
                        <span class='text-qty' :class='{red: item.TotalQty > 1}'>{{item.TotalQty}}</span>
                        <input type='checkbox' class='chk-done mt-1' v-model='item.Picked' @click.stop>
                    </div>
                </div>

                <div class='dropdown-list' :class='{open: item.ShowDetail}'>
                    <strong class='d-block mb-1 small text-warning'>CÁC ĐƠN CẦN LẤY:</strong>
                    <div>
                        <span v-for='id in item.OrderIds' class='tag-sn'>...{{id}}</span>
                    </div>
                </div>
            </div>
        </div>
    </div>

    <div class='modal fade' id='productModal' tabindex='-1'>
        <div class='modal-dialog modal-dialog-centered'>
            <div class='modal-content'>
                <div class='modal-header py-2 border-0'>
                    <h6 class='modal-title fw-bold text-danger flex-grow-1 text-center'>{{modalItem.ModelName}}</h6>
                    <button type='button' class='btn-close' data-bs-dismiss='modal'></button>
                </div>
                <div class='modal-body pt-0 text-center'>
                    <p class='text-muted small mb-2'>{{modalItem.ProductName}}</p>
                    <img :src='modalItem.ImageUrl' class='modal-img-main mb-3'>
                    
                    <div class='alert alert-primary py-1 px-3 d-inline-block fw-bold mb-3'>
                        Tồn kho hệ thống: {{modalItem.Stock || 99}}
                    </div>

                    <div class='text-start border-top pt-3'>
                        <small class='fw-bold text-secondary'><i class='bi bi-boxes'></i> SO SÁNH VỚI PHÂN LOẠI KHÁC:</small>
                        <div class='mt-2'>
                            <div class='comp-item active'>
                                <img :src='modalItem.ImageUrl' class='comp-img'>
                                <div class='flex-grow-1'>
                                    <div class='fw-bold text-primary'>{{modalItem.ModelName}}</div>
                                    <small class='text-muted'>[Đang chọn]</small>
                                </div>
                                <div class='comp-stock'>Tồn: {{modalItem.Stock || 99}}</div>
                            </div>
                            
                            <div v-for='v in mockVariations' class='comp-item'>
                                <img :src='v.Img' class='comp-img'>
                                <div class='flex-grow-1'>
                                    <div class='text-dark'>{{v.Name}}</div>
                                </div>
                                <div class='comp-stock' :class='{low: v.Stock < 5}'>Tồn: {{v.Stock}}</div>
                            </div>
                        </div>
                    </div>
                </div>
            </div>
        </div>
    </div>

</div>

<script src='https://cdn.jsdelivr.net/npm/bootstrap@5.3.0/dist/js/bootstrap.bundle.min.js'></script>
<script>
    const { createApp } = Vue;
    createApp({
        data() {
            return {
                orders: [],
                tab: 'unprocessed',
                currentView: 'manager',
                isBatchMode: false,
                sortDesc: true,
                openOrderId: null,
                batchItems: [],
                modalItem: {},
                mockVariations: []
            }
        },
        computed: {
            unprocessedOrders() { return this.orders.filter(o => o.Status === 0); },
            processedOrders() { return this.orders.filter(o => o.Status === 1); },
            filteredOrders() {
                let list = this.tab === 'unprocessed' ? this.unprocessedOrders : this.processedOrders;
                return list.sort((a,b) => this.sortDesc ? b.CreatedAt - a.CreatedAt : a.CreatedAt - b.CreatedAt);
            },
            selectedCount() { return this.orders.filter(o => o.Selected).length; },
            groupedBatch() {
                const groups = {};
                this.batchItems.forEach(i => {
                    const key = i.Location || 'Khác';
                    if(!groups[key]) groups[key] = [];
                    groups[key].push(i);
                });
                return Object.keys(groups).sort().reduce((obj, key) => { obj[key] = groups[key]; return obj; }, {});
            }
        },
        mounted() {
            this.fetchData();
            setInterval(this.fetchData, 10000);
        },
        methods: {
            async fetchData() {
                try {
                    const res = await fetch('/api/data');
                    const data = await res.json();
                    const selectedIds = new Set(this.orders.filter(o => o.Selected).map(o => o.OrderId));
                    this.orders = data.map(o => ({...o, Selected: selectedIds.has(o.OrderId)}));
                } catch(e) {}
            },
            toggleBatchMode() {
                this.isBatchMode = !this.isBatchMode;
                this.orders.forEach(o => o.Selected = false);
                this.openOrderId = null;
            },
            toggleOrder(id) {
                if (this.isBatchMode) {
                    const order = this.orders.find(o => o.OrderId === id);
                    if(order) order.Selected = !order.Selected;
                } else {
                    // Accordion logic: Chỉ mở 1 đơn 1 lúc
                    this.openOrderId = (this.openOrderId === id) ? null : id;
                }
            },
            showProductModal(item) {
                this.modalItem = item;
                // Giả lập dữ liệu
                this.mockVariations = [
                    { Name: 'Size L', Stock: 10, Img: item.ImageUrl },
                    { Name: 'Size M', Stock: 2, Img: item.ImageUrl }
                ];
                new bootstrap.Modal(document.getElementById('productModal')).show();
            },
            async shipOrder(orderId) {
                if(!confirm('Xác nhận in đơn?')) return;
                await fetch(`/api/ship?id=${orderId}`, {method: 'POST'});
                this.openOrderId = null;
                this.fetchData();
            },
            startPicking() {
                const selected = this.orders.filter(o => o.Selected);
                const agg = {};
                selected.forEach(order => {
                    order.Items.forEach(item => {
                        const key = item.ModelName + item.ParsedLocation; 
                        if(!agg[key]) agg[key] = {
                            ProductName: item.ProductName, ModelName: item.ModelName, ImageUrl: item.ImageUrl, 
                            Location: item.ParsedLocation, SKU: item.SKU, TotalQty: 0, OrderIds: [], Picked: false, ShowDetail: false
                        };
                        agg[key].TotalQty += item.Quantity;
                        const shortId = order.OrderId.slice(-4);
                        if(!agg[key].OrderIds.includes(shortId)) agg[key].OrderIds.push(shortId);
                    });
                });
                this.batchItems = Object.values(agg);
                this.currentView = 'picking';
            }
        }
    }).mount('#app');
</script>
</body>
</html>";
    }
}