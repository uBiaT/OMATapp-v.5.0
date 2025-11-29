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
    <title>Shopee WMS Pro</title>
    <link href='https://cdn.jsdelivr.net/npm/bootstrap@5.3.0/dist/css/bootstrap.min.css' rel='stylesheet'>
    <link rel='stylesheet' href='https://cdn.jsdelivr.net/npm/bootstrap-icons@1.11.0/font/bootstrap-icons.css'>
    <script src='https://unpkg.com/vue@3/dist/vue.global.js'></script>
    <style>
        body { background-color: #f4f6f8; padding-bottom: 100px; font-size: 14px; font-family: -apple-system, sans-serif; }
        .card-item { background: white; border-radius: 8px; box-shadow: 0 1px 3px rgba(0,0,0,0.05); margin-bottom: 10px; border: 1px solid #eee; overflow: hidden; }
        .card-body-custom { padding: 12px; display: flex; align-items: flex-start; position: relative; }
        .img-box { position: relative; width: 75px; height: 75px; flex-shrink: 0; margin-right: 12px; }
        .img-thumb { width: 100%; height: 100%; object-fit: cover; border-radius: 6px; border: 1px solid #eee; }
        .zoom-icon { position: absolute; bottom: 0; right: 0; background: rgba(0,0,0,0.6); color: white; font-size: 10px; padding: 2px 5px; border-radius: 4px 0 4px 0; }
        .info-box { flex-grow: 1; min-width: 0; display: flex; flex-direction: column; cursor: pointer; }
        .product-name { font-weight: 700; color: #222; margin-bottom: 4px; line-height: 1.3; font-size: 13px; display: -webkit-box; -webkit-line-clamp: 2; -webkit-box-orient: vertical; overflow: hidden; }
        .variation-badge { font-size: 12px; color: #e65100; background: #fff3e0; border: 1px solid #ffe0b2; padding: 1px 6px; border-radius: 4px; width: fit-content; margin-bottom: 4px; font-weight: 500; }
        .location-badge { font-size: 11px; color: #1565c0; background: #e3f2fd; padding: 1px 6px; border-radius: 4px; width: fit-content; font-weight: bold; border: 1px solid #bbdefb; }
        .expand-hint { font-size: 11px; color: #999; margin-top: 4px; display: flex; align-items: center; }
        .qty-box { text-align: right; padding-left: 10px; display: flex; flex-direction: column; align-items: flex-end; min-width: 40px; }
        .qty-text { font-size: 18px; font-weight: 800; color: #333; line-height: 1; margin-bottom: 8px; }
        .qty-text.red { color: #d32f2f; }
        .big-checkbox { width: 24px; height: 24px; cursor: pointer; accent-color: #2e7d32; margin-top: 2px; }
        .picking-orders { background: #fff8e1; padding: 10px; border-top: 1px dashed #ddd; display: none; animation: fadeIn 0.2s; }
        .picking-orders.show { display: block; }
        .sn-tag { font-family: monospace; font-size: 12px; background: white; border: 1px solid #e0e0e0; padding: 3px 8px; border-radius: 4px; margin-right: 5px; margin-bottom: 5px; display: inline-block; color: #555; box-shadow: 0 1px 2px rgba(0,0,0,0.05); }
        .picking-card.done { opacity: 0.5; background: #f5f5f5; border-color: #eee; }
        .picking-card.done .product-name { text-decoration: line-through; color: #999; }
        .picking-card.done .img-thumb { filter: grayscale(100%); }
        .picking-group-header { background: #ff9800; color: white; padding: 8px 12px; font-weight: bold; border-radius: 6px; margin-top: 15px; margin-bottom: 8px; font-size: 13px; display: flex; align-items: center; box-shadow: 0 2px 4px rgba(255, 152, 0, 0.3); }
        .order-header { padding: 12px; background: white; border-bottom: 1px solid #f0f0f0; cursor: pointer; display: flex; justify-content: space-between; align-items: center; border-radius: 8px; margin-bottom: 8px; box-shadow: 0 1px 2px rgba(0,0,0,0.05); }
        .order-header.active { background: #e8f5e9; border: 1px solid #81c784; margin-bottom: 0; border-radius: 8px 8px 0 0; }
        .sn-text { color: #555; font-family: monospace; font-size: 1.1em; letter-spacing: -0.5px; }
        .highlight-sn { color: #d32f2f; font-weight: 900; background: #ffebee; padding: 0 2px; border-radius: 2px; }
        .order-details-container { background: #fafafa; border: 1px solid #81c784; border-top: none; border-radius: 0 0 8px 8px; padding: 10px; margin-bottom: 10px; }
        .btn-float { position: fixed; bottom: 20px; left: 50%; transform: translateX(-50%); width: 90%; max-width: 500px; padding: 14px; border-radius: 50px; font-weight: bold; box-shadow: 0 4px 15px rgba(0,0,0,0.3); z-index: 1000; font-size: 16px; border: none; }
        @keyframes fadeIn { from { opacity: 0; } to { opacity: 1; } }
    </style>
</head>
<body>
<div id='app' class='container py-2' style='max-width: 600px'>
    <div class='d-flex justify-content-between align-items-center mb-3 bg-white p-3 rounded shadow-sm sticky-top'>
        <span class='fw-bold text-primary h5 mb-0'><i class='bi bi-box-seam-fill'></i> KHO HÀNG</span>
        <button class='btn btn-sm btn-light border' @click='fetchData'><i class='bi bi-arrow-clockwise'></i></button>
    </div>

    <div v-if='currentView === ""manager""'>
        <ul class='nav nav-pills nav-fill mb-3 bg-white p-1 rounded shadow-sm'>
            <li class='nav-item'><a class='nav-link' :class='{active: tab===""unprocessed""}' @click='tab=""unprocessed""'>Chờ xử lý ({{unprocessedOrders.length}})</a></li>
            <li class='nav-item'><a class='nav-link' :class='{active: tab===""processed""}' @click='tab=""processed""'>Đã xử lý ({{processedOrders.length}})</a></li>
        </ul>

        <div class='d-flex justify-content-between mb-3 align-items-center' v-if='tab===""unprocessed""'>
            <button class='btn btn-sm btn-white border shadow-sm' @click='sortDesc = !sortDesc'><i class='bi' :class='sortDesc ? ""bi-sort-down"" : ""bi-sort-up""'></i> {{sortDesc ? 'Mới nhất' : 'Cũ nhất'}}</button>
            <button class='btn btn-sm fw-bold shadow-sm' :class='isBatchMode ? ""btn-danger"" : ""btn-warning""' @click='toggleBatchMode'>{{ isBatchMode ? '❌ Hủy chọn' : '📦 Gom đơn' }}</button>
        </div>

        <div v-for='order in filteredOrders' :key='order.OrderId'>
            <div class='order-header' :class='{active: openOrderId === order.OrderId}' @click='toggleOrder(order.OrderId)'>
                <div class='d-flex align-items-center'>
                    <input v-if='isBatchMode' type='checkbox' class='form-check-input me-3 big-checkbox' v-model='order.Selected' @click.stop>
                    <span class='sn-text'>{{order.OrderId.slice(0, -4)}}<span class='highlight-sn'>{{order.OrderId.slice(-4)}}</span></span>
                </div>
                <div class='d-flex align-items-center gap-2'>
                    <span class='badge bg-secondary rounded-pill'>{{order.Items.length}} loại</span>
                    <i class='bi' :class='openOrderId === order.OrderId ? ""bi-chevron-up"" : ""bi-chevron-down""'></i>
                </div>
            </div>

            <div v-if='openOrderId === order.OrderId' class='order-details-container'>
                <div v-for='item in order.Items' class='card-item border-0 mb-2'>
                    <div class='card-body-custom p-0 pb-2 border-bottom mb-2'>
                        <div class='img-box' @click.stop='showProductModal(item)'><img :src='item.ImageUrl' class='img-thumb'><div class='zoom-icon'><i class='bi bi-eye-fill'></i></div></div>
                        <div class='info-box'>
                            <div class='product-name'>{{item.ProductName}}</div>
                            <div class='variation-badge'>{{item.ModelName}}</div>
                            <div class='location-badge'><i class='bi bi-geo-alt-fill'></i> {{item.ParsedLocation}}</div>
                        </div>
                        <div class='qty-box'><span class='qty-text' :class='{red: item.Quantity > 1}'>x{{item.Quantity}}</span></div>
                    </div>
                </div>
                <div class='d-flex gap-2 mt-2' v-if='!isBatchMode && order.Status === 0'>
                    <div class='dropdown flex-grow-1'>
                        <button class='btn btn-outline-secondary w-100 btn-sm' data-bs-toggle='dropdown'>👤 {{order.AssignedTo || 'Phân công'}}</button>
                        <ul class='dropdown-menu w-100'><li v-for='u in users'><a class='dropdown-item' @click='assignUser(order.OrderId, u)'>{{u}}</a></li></ul>
                    </div>
                    <button class='btn btn-danger flex-grow-1 fw-bold btn-sm' @click='shipOrder(order.OrderId)'><i class='bi bi-printer-fill'></i> IN ĐƠN</button>
                </div>
            </div>
        </div>
        <button v-if='isBatchMode && selectedCount > 0' class='btn btn-warning btn-float text-white' @click='startPicking'>BẮT ĐẦU SOẠN ({{selectedCount}}) <i class='bi bi-arrow-right'></i></button>
    </div>

    <div v-if='currentView === ""picking""'>
        <div class='sticky-top bg-white p-3 shadow-sm d-flex justify-content-between align-items-center mb-3'>
            <button class='btn btn-outline-secondary btn-sm' @click='currentView=""manager""'>Thoát</button>
            <span class='fw-bold text-warning'>LỘ TRÌNH ĐI NHẶT</span>
            <span class='badge bg-warning text-dark'>{{batchItems.length}} dòng</span>
        </div>
        <div v-for='(group, loc) in groupedBatch' :key='loc'>
            <div class='picking-group-header'><i class='bi bi-geo-alt-fill me-1'></i> {{loc}}</div>
            <div v-for='item in group' class='card-item picking-card' :class='{done: item.Picked}'>
                <div class='card-body-custom'>
                    <div class='img-box' @click.stop='showProductModal(item)'><img :src='item.ImageUrl' class='img-thumb'><div class='zoom-icon'><i class='bi bi-eye-fill'></i></div></div>
                    <div class='info-box' @click='item.ShowDetail = !item.ShowDetail'>
                        <div class='product-name'>{{item.ProductName}}</div>
                        <div class='variation-badge'>{{item.ModelName}}</div>
                        <div class='expand-hint' v-if='!item.ShowDetail'><i class='bi bi-caret-down-fill me-1'></i> Xem {{item.OrderIds.length}} đơn</div>
                        <div class='expand-hint' v-else><i class='bi bi-caret-up-fill me-1'></i> Thu gọn</div>
                    </div>
                    <div class='qty-box'>
                        <span class='qty-text' :class='{red: item.TotalQty > 1}'>{{item.TotalQty}}</span>
                        <input type='checkbox' class='big-checkbox mt-1' v-model='item.Picked' @click.stop>
                    </div>
                </div>
                <div class='picking-orders' :class='{show: item.ShowDetail}'>
                    <strong>DANH SÁCH ĐƠN:</strong><div class='d-flex flex-wrap gap-1 mt-1'><span v-for='id in item.OrderIds' class='sn-tag'>...{{id}}</span></div>
                </div>
            </div>
        </div>
    </div>

    <div class='modal fade' id='productModal' tabindex='-1'>
        <div class='modal-dialog modal-dialog-centered modal-sm'>
            <div class='modal-content'>
                <div class='modal-header border-0 pb-0 pt-2'><h6 class='modal-title fw-bold text-danger w-100 text-center'>{{modalItem.ModelName}}</h6></div>
                <div class='modal-body text-center pt-1'>
                    <p class='text-muted small mb-2'>{{modalItem.ProductName}}</p>
                    <img :src='modalItem.ImageUrl' class='w-100 rounded mb-3 border shadow-sm'>
                    <div class='alert alert-primary py-1 px-3 d-inline-block fw-bold mb-3'>Tồn kho hệ thống: {{modalItem.Stock || 99}}</div>
                    <div class='text-start border-top pt-2'>
                        <small class='text-muted fw-bold'>Phân loại khác:</small>
                        <div class='list-group list-group-flush mt-1'>
                            <div class='list-group-item px-0 py-2 d-flex justify-content-between align-items-center' v-for='v in mockVariations'>
                                <div class='d-flex align-items-center'><img :src='v.Img' style='width:35px;height:35px' class='rounded me-2 border'><span class='small fw-bold'>{{v.Name}}</span></div>
                                <span class='badge bg-secondary'>{{v.Stock}}</span>
                            </div>
                        </div>
                    </div>
                </div>
                <div class='modal-footer border-0 p-1'><button type='button' class='btn btn-secondary w-100' data-bs-dismiss='modal'>Đóng</button></div>
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
                orders: [], tab: 'unprocessed', currentView: 'manager', isBatchMode: false, sortDesc: true, openOrderId: null, batchItems: [],
                users: ['Tài', 'Oanh', 'My', 'Mẹ Hà'], modalItem: {}, mockVariations: []
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
        mounted() { this.fetchData(); setInterval(this.fetchData, 10000); },
        methods: {
            async fetchData() {
                try {
                    const res = await fetch('/api/data');
                    const data = await res.json();
                    const selectedIds = new Set(this.orders.filter(o => o.Selected).map(o => o.OrderId));
                    this.orders = data.map(o => ({...o, Selected: selectedIds.has(o.OrderId)}));
                } catch(e) {}
            },
            toggleBatchMode() { this.isBatchMode = !this.isBatchMode; this.orders.forEach(o => o.Selected = false); this.openOrderId = null; },
            toggleOrder(id) {
                if (this.isBatchMode) { const order = this.orders.find(o => o.OrderId === id); if(order) order.Selected = !order.Selected; } 
                else { this.openOrderId = (this.openOrderId === id) ? null : id; }
            },
            togglePickDetail(item) { item.ShowDetail = !item.ShowDetail; },
            async assignUser(orderId, user) { await fetch(`/api/assign?id=${orderId}&user=${encodeURIComponent(user)}`, {method: 'POST'}); this.fetchData(); },
            showProductModal(item) {
                this.modalItem = item;
                this.mockVariations = [{ Name: 'Xanh - L', Stock: 5, Img: item.ImageUrl }, { Name: 'Đỏ - M', Stock: 0, Img: item.ImageUrl }];
                new bootstrap.Modal(document.getElementById('productModal')).show();
            },
            async shipOrder(orderId) {
                if(!confirm('Xác nhận in đơn?')) return;
                await fetch(`/api/ship?id=${orderId}`, {method: 'POST'});
                this.openOrderId = null; this.fetchData();
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