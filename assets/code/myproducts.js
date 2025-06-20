import {
    doc,
    deleteField,
    updateDoc,
    setDoc,
    getDoc,
    arrayUnion,
} from "https://www.gstatic.com/firebasejs/9.22.2/firebase-firestore.js";
import { db, auth } from "./checkAuth.js";
import { onAuthStateChanged } from "https://www.gstatic.com/firebasejs/9.22.2/firebase-auth.js";

if ("serviceWorker" in navigator) {
    window.addEventListener("load", async () => {
        await navigator.serviceWorker.register(
            "/assets/code/service-worker.js",
        );
    });
}

function createProductItemHTML(id, name, quantity, unit) {
    const escapedName = name.replace(/</g, "&lt;").replace(/>/g, "&gt;");
    const formattedQuantity = String(quantity).replace(".", ",");
    const escapedQuantity = formattedQuantity
        .replace(/</g, "&lt;")
        .replace(/>/g, "&gt;");
    const escapedUnit = unit.replace(/</g, "&lt;").replace(/>/g, "&gt;");
    const quantityForInput = String(quantity);

    return `
            <div class="product-info">
                <span class="product-name">${escapedName}</span>
                <span class="product-quantity-display">${escapedQuantity} ${escapedUnit}</span>
                <div class="edit-quantity-controls" hidden>
                    <input type="text" class="product-name-input" value="${escapedName}">
                    <input type="number" class="product-quantity-input" value="${quantityForInput}" min="1" step="any">
                    <select class="product-unit-select">
                        <option value="шт" ${escapedUnit === "шт" ? "selected" : ""}>шт</option>
                        <option value="гр" ${escapedUnit === "гр" ? "selected" : ""}>гр</option>
                    </select>
                </div>
            </div>
            <div class="product-actions">
                    <button class="edit-btn"><img src="assets/svg/edit.svg" alt="Edit"></button>
                    <button class="save-btn" hidden><img src="assets/svg/save.svg" alt="Save"></button>
                    <button class="cancel-btn" hidden><img src="assets/svg/cancel.svg" alt="Cancel"></button>
                    <button class="delete-btn"><img src="assets/svg/trash.svg" alt="Delete"></button>
            </div>
        `;
}

async function addProductToList(name, quantity, unit) {
    const newItem = document.createElement("div");
    newItem.classList.add("product-item");
    const uniqueId = Date.now();
    newItem.dataset.id = uniqueId;
    newItem.innerHTML = createProductItemHTML(uniqueId, name, quantity, unit);
    productList.appendChild(newItem);
    updateProductDb(name, quantity, unit);
}

async function toggleEditMode(item, isEditing) {
    const nameSpan = item.querySelector(".product-name");
    const oldName = nameSpan.textContent;
    const quantityDisplaySpan = item.querySelector(".product-quantity-display");
    const nameInput = item.querySelector(".product-name-input");
    const quantityInput = item.querySelector(".product-quantity-input");
    const unitSelect = item.querySelector(".product-unit-select");

    if (isEditing) {
        nameInput.value = nameSpan.textContent;
        const quantityText = quantityDisplaySpan.textContent;
        const parts = quantityText.match(/^([\d.,]+)\s*(\S+)$/);
        if (parts && parts.length === 3) {
            quantityInput.value = parts[1].replace(",", ".");
            unitSelect.value = parts[2];
        }
        item.classList.add("edit-mode");
        nameInput.focus();
        nameInput.select();
    } else {
        const newName = nameInput.value.trim();
        const newQuantityStr = quantityInput.value.trim();
        const newUnit = unitSelect.value;

        if (!newName || !newQuantityStr) {
            alert("Название и количество не должны быть пустыми.");
            nameInput.focus();
            return false;
        }

        const newQuantity = parseFloat(newQuantityStr.replace(",", "."));
        if (isNaN(newQuantity) || newQuantity < 0) {
            alert("Количество должно быть положительным числом.");
            quantityInput.focus();
            return false;
        }

        nameSpan.textContent = newName;
        quantityDisplaySpan.textContent = `${String(newQuantity).replace(".", ",")} ${newUnit}`;
        item.classList.remove("edit-mode");
        deleteProductDb(oldName);
        updateProductDb(newName, newQuantityStr, newUnit);
        return true;
    }
    return true;
}

function handleEdit(button) {
    const item = button.closest(".product-item");
    if (item) toggleEditMode(item, true);
}

function handleSave(button) {
    const item = button.closest(".product-item");
    if (item) toggleEditMode(item, false);
}

function handleDelete(button) {
    const item = button.closest(".product-item");
    if (!item) return;
    const productName = item.querySelector(".product-name").textContent;
    if (confirm(`Удалить продукт "${productName}"?`)) {
        item.remove();
        deleteProductDb(productName);
    }
}

function handleCancel(button) {
    const item = button.closest(".product-item");
    if (!item) return;
    item.classList.remove("edit-mode");
}

async function updateProductDb(productName, quantity, unit) {
    const userDocRef = doc(db, "users", auth.currentUser.uid);
    await updateDoc(userDocRef, {
        [`products.${productName}`]: `${quantity} ${unit}`,
    });
}

async function deleteProductDb(productName) {
    const userDocRef = doc(db, "users", auth.currentUser.uid);
    await updateDoc(userDocRef, {
        [`products.${productName}`]: deleteField(),
    });
}

async function clearAllProducts(params) {
    const userDocRef = doc(db, "users", auth.currentUser.uid);
    await setDoc(userDocRef, {
        products: {},
    });
}

async function getAllProducts() {
    const userDocRef = doc(db, "users", auth.currentUser.uid);
    const data = await getDoc(userDocRef);
    if (data.exists()) {
        return data.data().products;
    }
}

export { getAllProducts };

async function showProducts() {
    const products = await getAllProducts();
    for (let productName in products) {
        const value = products[productName].split(" ");
        addProductToList(productName, value[0], value[1]);
    }
}

document.addEventListener("DOMContentLoaded", () => {
    const showAddFormBtn = document.getElementById("showAddFormBtn");
    const addProductFormContainer = document.querySelector(".add-product-form");
    const saveNewProductBtn = document.getElementById("saveNewProductBtn");
    const cancelNewProductBtn = document.getElementById("cancelNewProductBtn");
    const newProductNameInput = document.getElementById("newProductName");
    const newProductQuantityInput =
        document.getElementById("newProductQuantity");
    const newProductUnitSelect = document.getElementById("newProductUnit");
    const productList = document.getElementById("productList");
    const clearAllBtn = document.getElementById("clearAllBtn");

    showAddFormBtn.addEventListener("click", () => {
        if (!addProductFormContainer.hidden) return;
        addProductFormContainer.hidden = false;
        showAddFormBtn.hidden = true;
        newProductNameInput.value = "";
        newProductQuantityInput.value = "";
        newProductUnitSelect.value = "шт";
        newProductNameInput.focus();
        addProductFormContainer.scrollIntoView({ behavior: "smooth" });
    });

    cancelNewProductBtn.addEventListener("click", () => {
        addProductFormContainer.hidden = true;
        showAddFormBtn.hidden = false;
    });

    saveNewProductBtn.addEventListener("click", () => {
        const name = newProductNameInput.value.trim();
        const quantity = newProductQuantityInput.value.trim();
        const unit = newProductUnitSelect.value;

        if (!name || !quantity) {
            alert("Пожалуйста, введите название и количество продукта.");
            return;
        }

        const quantityNum = parseFloat(quantity.replace(",", "."));
        if (isNaN(quantityNum) || quantityNum < 0) {
            alert("Количество должно быть положительным числом.");
            newProductQuantityInput.focus();
            return;
        }

        addProductToList(name, quantityNum, unit);
        addProductFormContainer.hidden = true;
        showAddFormBtn.hidden = false;
    });

    clearAllBtn.addEventListener("click", () => {
        if (productList.children.length === 0) return;
        if (confirm("Вы уверены, что хотите удалить все продукты?")) {
            clearAllProducts();
            productList.innerHTML = "";
        }
    });

    productList.addEventListener("click", (event) => {
        const target = event.target;
        const editButton = target.closest(".edit-btn");
        const saveButton = target.closest(".save-btn");
        const deleteButton = target.closest(".delete-btn");
        const cancelButton = target.closest(".cancel-btn");

        if (editButton) handleEdit(editButton);
        else if (saveButton) handleSave(saveButton);
        else if (cancelButton) handleCancel(cancelButton);
        else if (deleteButton) handleDelete(deleteButton);
    });

    onAuthStateChanged(auth, async (user) => {
        if (user) {
            showProducts();
            addProductFormContainer.hidden = true;
            showAddFormBtn.hidden = false;
        } else {
            const recipeCardsContainer =
                document.querySelectorAll(".recipe-container")[0];
            if (recipeCardsContainer) {
                recipeCardsContainer.innerHTML = "";
            }
        }
    });
});
