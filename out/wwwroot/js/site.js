// 全域庫存管理函數
const InventoryApp = {
    // 格式化數字
    formatNumber: function(num) {
        return new Intl.NumberFormat().format(num);
    },

    // 計算庫存覆蓋天數
    calculateCoverageDays: function(currentStock, weeklyDemand, safetyStock) {
        if (weeklyDemand === 0 || isNaN(weeklyDemand)) {
            return 'stock-status-normal';
        }
        
        const coverageDays = (currentStock / weeklyDemand) * 7;
        
        if (coverageDays === 0) {
            return 'stock-status-critical';
        } else if (coverageDays < 3 || currentStock < safetyStock) {
            return 'stock-status-low';
        } else if (coverageDays > 30) {
            return 'stock-status-overstock';
        } else {
            return 'stock-status-normal';
        }
    },

    // 安全庫存檢查
    checkSafeStock: async function(flowerId) {
        try {
            const response = await fetch(`/Workflow/GetFlowerStockInfo?flowerId=${flowerId}`);
            const data = await response.json();
            return data;
        } catch (error) {
            console.error('安全庫存檢查失敗', error);
            return null;
        }
    },

    // 顯示 Toast 訊息
    showToast: function(message, type = 'info') {
        // 建立 Toast 容器（如果不存在）
        let toastContainer = document.getElementById('toast-container');
        if (!toastContainer) {
            toastContainer = document.createElement('div');
            toastContainer.id = 'toast-container';
            toastContainer.className = 'toast-container position-fixed top-0 end-0 p-3';
            document.body.appendChild(toastContainer);
        }

        // 建立 Toast
        const toastId = 'toast-' + Date.now();
        const toast = document.createElement('div');
        toast.id = toastId;
        toast.className = `toast align-items-center text-bg-${type} border-0`;
        toast.innerHTML = `
            <div class="d-flex">
                <div class="toast-body">
                    ${message}
                </div>
                <button type="button" class="btn-close btn-close-white me-2 m-auto" data-bs-dismiss="toast"></button>
            </div>
        `;

        toastContainer.appendChild(toast);
        
        // 顯示 Toast
        const bsToast = new bootstrap.Toast(toast);
        bsToast.show();

        // 自動移除
        toast.addEventListener('hidden.bs.toast', () => {
            toast.remove();
        });
    },

    // 顯示載入中狀態
    showLoading: function(show = true) {
        let loadingOverlay = document.getElementById('loading-overlay');
        
        if (show) {
            if (!loadingOverlay) {
                loadingOverlay = document.createElement('div');
                loadingOverlay.id = 'loading-overlay';
                loadingOverlay.className = 'loading-overlay';
                loadingOverlay.innerHTML = `
                    <div class="text-center">
                        <div class="loading-spinner"></div>
                        <p class="mt-2">處理中...</p>
                    </div>
                `;
                document.body.appendChild(loadingOverlay);
            }
            loadingOverlay.style.display = 'flex';
        } else if (loadingOverlay) {
            loadingOverlay.style.display = 'none';
        }
    },

    // 表單提交處理
    handleFormSubmit: async function(formElement, successCallback) {
        try {
            this.showLoading(true);
            
            const formData = new FormData(formElement);
            const response = await fetch(formElement.action, {
                method: formElement.method,
                body: formData
            });

            if (response.ok) {
                if (successCallback) {
                    successCallback(await response.json());
                }
                this.showToast('操作成功', 'success');
            } else {
                throw new Error('操作失敗');
            }
        } catch (error) {
            console.error('表單提交錯誤', error);
            this.showToast('操作失敗，請稍後再試', 'danger');
        } finally {
            this.showLoading(false);
        }
    }
};

// 頁面載入完成後初始化
document.addEventListener('DOMContentLoaded', function() {
    // 自動計算相關欄位
    const autoCalculateFields = document.querySelectorAll('[data-auto-calculate]');
    autoCalculateFields.forEach(field => {
        field.addEventListener('change', function() {
            const targetId = this.dataset.autoCalculate;
            if (targetId) {
                const targetElement = document.querySelector(targetId);
                if (targetElement) {
                    // 根據欄位類型執行計算
                    switch (this.type) {
                        case 'number':
                            targetElement.value = this.value * 1.1; // 示例計算
                            break;
                        case 'date':
                            // 日期計算邏輯
                            break;
                    }
                }
            }
        });
    });

    // 表單提前驗證
    const forms = document.querySelectorAll('form[data-validate="true"]');
    forms.forEach(form => {
        form.addEventListener('submit', function(e) {
            if (!form.checkValidity()) {
                e.preventDefault();
                e.stopPropagation();
            }
            form.classList.add('was-validated');
        });
    });

    // 庫存狀態視覺化
    initializeStockVisualization();
});

// 庫存狀態視覺化初始化
function initializeStockVisualization() {
    const stockElements = document.querySelectorAll('[data-stock-status]');
    
    stockElements.forEach(element => {
        const status = element.dataset.stockStatus;
        const indicator = document.createElement('span');
        indicator.className = `stock-indicator ${status}`;
        element.prepend(indicator);
    });
}

// 響應式表格處理
function initializeResponsiveTables() {
    const tables = document.querySelectorAll('.table-responsive');
    
    tables.forEach(table => {
        // 為行動裝置添加特殊處理
        if (window.innerWidth < 768) {
            table.classList.add('mobile-table-card');
        }
    });
}

// 視窗大小改變時重新初始化
window.addEventListener('resize', initializeResponsiveTables);
