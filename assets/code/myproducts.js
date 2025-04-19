document.addEventListener('DOMContentLoaded', () => {
    // Получаем элементы DOM
    const addProductBtn = document.getElementById('addProductBtn');
    const scanCheckBtn = document.getElementById('scanCheckBtn'); // Пока не используется
    const productList = document.getElementById('productList');
    const clearAllBtn = document.getElementById('clearAllBtn');

    // --- ОБРАБОТЧИКИ СОБЫТИЙ ---

    // Нажатие кнопки "Добавить продукт"
    addProductBtn.addEventListener('click', () => {
        addNewProductItem();
    });

    // Нажатие кнопки "Удалить все продукты"
    clearAllBtn.addEventListener('click', () => {
        // Добавим подтверждение для безопасности
        if (confirm('Вы уверены, что хотите удалить все продукты?')) {
            productList.innerHTML = ''; // Очищаем список
        }
    });

    // Используем делегирование событий для кнопок внутри списка
    productList.addEventListener('click', (event) => {
        const target = event.target; // Элемент, по которому кликнули

        // Ищем ближайшую кнопку (edit, save, delete) или ее родителя, если клик был по img
        const editButton = target.closest('.edit-btn');
        const saveButton = target.closest('.save-btn');
        const deleteButton = target.closest('.delete-btn');

        if (editButton) {
            handleEdit(editButton);
        } else if (saveButton) {
            handleSave(saveButton);
        } else if (deleteButton) {
            handleDelete(deleteButton);
        }
    });

    // --- ФУНКЦИИ ---

    // Создание HTML-структуры для нового элемента продукта
    function createProductItemHTML(id, name = '', quantity = '') {
        // Используем уникальный id, например, timestamp
        const uniqueId = id || Date.now();
        return `
            <div class="product-info">
                <span class="product-name">${name}</span>
                <input type="text" class="product-name-input" value="${name}" placeholder="Название продукта" hidden>
                <span class="product-quantity">${quantity}</span>
                <input type="number" class="product-quantity-input" value="${quantity}" placeholder="Количество" hidden>
            </div>
            <div class="product-actions">
                <button class="edit-btn"><img src="assets/svg/edit.svg" alt="Edit"></button>
                <button class="save-btn" hidden><img src="assets/svg/save.svg" alt="Save"></button>
                <button class="delete-btn"><img src="assets/svg/trash.svg" alt="Delete"></button>
            </div>
        `;
    }

    // Добавление нового пустого продукта в список
    function addNewProductItem() {
        const newItem = document.createElement('div');
        newItem.classList.add('product-item');
        const uniqueId = Date.now(); // Генерируем ID
        newItem.dataset.id = uniqueId; // Устанавливаем data-атрибут
        newItem.innerHTML = createProductItemHTML(uniqueId, '', ''); // Создаем с пустыми значениями

        productList.appendChild(newItem);

        // Сразу переключаем новый элемент в режим редактирования
        toggleEditMode(newItem, true);
         // Устанавливаем фокус на первое поле ввода
        const nameInput = newItem.querySelector('.product-name-input');
        if (nameInput) {
            nameInput.focus();
        }
    }

    // Переключение режима редактирования для элемента
    function toggleEditMode(item, isEditing) {
         const nameSpan = item.querySelector('.product-name');
         const quantitySpan = item.querySelector('.product-quantity');
         const nameInput = item.querySelector('.product-name-input');
         const quantityInput = item.querySelector('.product-quantity-input');
         // const editBtn = item.querySelector('.edit-btn'); // Кнопки скрываются/показываются через CSS
         // const saveBtn = item.querySelector('.save-btn');

        if (isEditing) {
             // Включаем режим редактирования
             // Копируем текст из span в input перед показом input
             nameInput.value = nameSpan.textContent;
             quantityInput.value = quantitySpan.textContent;

             item.classList.add('edit-mode'); // Добавляем класс для CSS стилей
             nameInput.hidden = false;       // Показываем инпуты
             quantityInput.hidden = false;
             // editBtn.hidden = true;       // Скрываем/показываем кнопки (управляется CSS)
             // saveBtn.hidden = false;

        } else {
             // Выключаем режим редактирования
             // Копируем текст из input в span перед показом span (если инпуты не пустые)
             if (nameInput.value.trim() !== '' || quantityInput.value.trim() !== '') {
                nameSpan.textContent = nameInput.value.trim() || 'Без названия'; // Ставим заглушку если пусто
                quantitySpan.textContent = quantityInput.value.trim() || '-';
             } else {
                 // Если оба поля пустые после "сохранения", можно удалить элемент
                 // Или оставить с заглушками, как выше. Пока удалим.
                 if (confirm('Продукт не заполнен. Удалить его?')) {
                     item.remove();
                     return; // Прерываем выполнение функции
                 } else {
                     // Оставляем в режиме редактирования, если пользователь отменил удаление
                     return;
                 }

             }


             item.classList.remove('edit-mode'); // Убираем класс
              nameInput.hidden = true;       // Скрываем инпуты
              quantityInput.hidden = true;
             // editBtn.hidden = false;      // Скрываем/показываем кнопки (управляется CSS)
             // saveBtn.hidden = true;
        }
    }

    // Обработка нажатия кнопки "Редактировать"
    function handleEdit(button) {
        const item = button.closest('.product-item');
        if (!item) return;
        toggleEditMode(item, true); // Включаем режим редактирования
        // Ставим фокус на поле ввода имени
        const nameInput = item.querySelector('.product-name-input');
        if(nameInput) {
            nameInput.focus();
            nameInput.select(); // Выделяем текст для удобства
        }
    }

    // Обработка нажатия кнопки "Сохранить"
    function handleSave(button) {
        const item = button.closest('.product-item');
        if (!item) return;
        toggleEditMode(item, false); // Выключаем режим редактирования
    }

    // Обработка нажатия кнопки "Удалить"
    function handleDelete(button) {
        const item = button.closest('.product-item');
        if (!item) return;

        // Добавим подтверждение
        if (confirm(`Удалить продукт "${item.querySelector('.product-name').textContent}"?`)) {
             item.remove(); // Удаляем элемент из DOM
        }
    }

    // --- ИНИЦИАЛИЗАЦИЯ ---
    // (Можно добавить загрузку продуктов из localStorage, если нужно)

}); // Конец DOMContentLoaded