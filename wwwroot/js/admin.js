window.addEventListener('load', function () {
    document.querySelector('.admin-body').style.display = 'block';
});



(function () {
    'use strict';

    $(initSidebar);
    $(loadOrders)
    function initSidebar() {
        $(document).ready(function () {
            $('#sidebarCollapse').on('click', function () {
                $('#sidebar').toggleClass('active');
            });
        });
    }
    function loadOrders() {
        fetch('/Admin/Requests/LoadRequests')
            .then(response => response.json())
            .then(data => {
                if (data.success) {
                    document.querySelector('.order-count').textContent = data.totalQuantity;
                }
            })
            .catch(error => console.error('Error loading orders:', error));
    }
    setInterval(loadOrders, 10000);

})();