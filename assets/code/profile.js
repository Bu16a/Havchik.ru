import {getAuth, signOut, onAuthStateChanged} from "https://www.gstatic.com/firebasejs/9.22.2/firebase-auth.js";
import {
    doc,
    updateDoc,
    arrayUnion,
    getDoc,
    arrayRemove
} from "https://www.gstatic.com/firebasejs/9.22.2/firebase-firestore.js";

import {app, auth, db} from "./checkAuth.js"

const logOutButton = document.getElementById("logOut");
const showAllergensBtn = document.getElementById('showAllergensBtn');
const showCookedRecipesBtn = document.getElementById('showCookedRecipesBtn');
const allergensContent = document.getElementById('allergensContent');
const cookedRecipesContent = document.getElementById('cookedRecipesContent');
const allergenListDiv = document.querySelector('.allergen-list');
const cookedRecipesListDiv = document.querySelector('.cooked-recipes-list');

const showAddAllergenFormBtn = document.getElementById('showAddAllergenFormBtn');
const addAllergenForm = document.querySelector('.add-allergen-form');
const newAllergenNameInput = document.getElementById('newAllergenName');
const saveNewAllergenBtn = document.getElementById('saveNewAllergenBtn');
const cancelNewAllergenBtn = document.getElementById('cancelNewAllergenBtn');

if ('serviceWorker' in navigator) {
    window.addEventListener('load', async () => {
        await navigator.serviceWorker.register('/assets/code/service-worker.js');
    });
}

function generateAllergenId() {
    return Date.now().toString(36) + Math.random().toString(36).substring(2);
}

async function saveAllergenDB(allergenName) {
    if (!auth.currentUser) {
        console.error("User not authenticated to save allergen.");
        alert("Пожалуйста, войдите в систему, чтобы сохранить продукт.");
        return;
    }
    const userDocRef = doc(db, "users", auth.currentUser.uid);
    const allergenId = generateAllergenId();
    try {
        await updateDoc(userDocRef, {
            allergens: arrayUnion({name: allergenName, id: allergenId})
        });
        await loadUserAllergens();
    } catch (error) {
        console.error("Error saving allergen:", error);
        alert("Ошибка при сохранении продукта.");
    }
}

export async function getAllergens() {
    if (!auth.currentUser) {
        return [];
    }
    const userDocRef = doc(db, "users", auth.currentUser.uid);
    try {
        const docSnap = await getDoc(userDocRef);
        if (docSnap.exists() && docSnap.data().allergens) {
            return docSnap.data().allergens;
        } else {
            return [];
        }
    } catch (error) {
        console.error("Error fetching allergens:", error);
        return [];
    }
}

async function removeAllergenDB(allergen) {
    if (!auth.currentUser) {
        console.error("User not authenticated to remove allergen.");
        alert("Пожалуйста, войдите в систему, чтобы удалить продукт.");
        return;
    }
    const userDocRef = doc(db, "users", auth.currentUser.uid);
    try {
        await updateDoc(userDocRef, {
            allergens: arrayRemove(allergen)
        });
        await loadUserAllergens();
    } catch (error) {
        console.error("Error removing allergen:", error);
        alert("Ошибка при удалении продукта.");
    }
}

function displayAllergens(allergens) {
    if (!allergenListDiv) return;
    allergenListDiv.innerHTML = '';

    if (!allergens || allergens.length === 0) {
        allergenListDiv.innerHTML = '<p><em>У вас пока нет добавленных продуктов.</em></p>';
        return;
    }

    const ul = document.createElement('ul');
    ul.classList.add('profile-item-list');
    allergens.forEach(allergen => {
        const li = document.createElement('li');
        li.classList.add('profile-item');
        li.textContent = allergen.name;

        const deleteBtn = document.createElement('button');
        const img = document.createElement("img");
        img.src = "assets/svg/trash.svg";
        img.alt = "Удалить";
        deleteBtn.appendChild(img);
        deleteBtn.classList.add('delete-item-btn');
        deleteBtn.onclick = () => removeAllergenDB(allergen);

        li.appendChild(deleteBtn);
        ul.appendChild(li);
    });
    allergenListDiv.appendChild(ul);
}

async function loadUserAllergens() {
    const allergens = await getAllergens();
    displayAllergens(allergens);
}

async function getCookedRecipes() {
    if (!auth.currentUser) {
        return [];
    }
    const userDocRef = doc(db, "users", auth.currentUser.uid);
    try {
        const docSnap = await getDoc(userDocRef);
        if (docSnap.exists() && docSnap.data().cooked) {
            return docSnap.data().cooked;
        } else {
            return [];
        }
    } catch (error) {
        console.error("Error fetching cooked recipes:", error);
        return [];
    }
}

function displayCookedRecipes(recipes) {
    if (!cookedRecipesListDiv) return;
    cookedRecipesListDiv.innerHTML = '';

    if (!recipes || recipes.length === 0) {
        cookedRecipesListDiv.innerHTML = '<p><em>Вы еще не готовили ни одного рецепта.</em></p>';
        return;
    }

    const ul = document.createElement('ul');
    ul.classList.add('profile-item-list');
    recipes.forEach(recipe => {
        const li = document.createElement('li');
        li.classList.add('profile-item');
        if (recipe.id && recipe.name) {
            const link = document.createElement('a');
            link.href = `/recipe.html?id=${recipe.id}`;
            link.textContent = recipe.name;
            li.appendChild(link);
        } else {
            li.textContent = recipe.name || "Неизвестный рецепт";
        }
        ul.appendChild(li);
    });
    cookedRecipesListDiv.appendChild(ul);
}

async function loadUserCookedRecipes() {
    const recipes = await getCookedRecipes();
    displayCookedRecipes(recipes);
}


if (logOutButton) {
    logOutButton.addEventListener('click', (e) => {
        e.preventDefault();
        signOut(auth).then(() => {
            window.location.href = '/auth.html';
        }).catch((error) => {
            console.error("Ошибка выхода:", error);
        });
    });
}

if (showAllergensBtn) {
    showAllergensBtn.addEventListener('click', async () => {
        allergensContent.classList.add('active');
        cookedRecipesContent.classList.remove('active');
        showAllergensBtn.classList.add('active');
        showCookedRecipesBtn.classList.remove('active');
        await loadUserAllergens();
    });
}

if (showCookedRecipesBtn) {
    showCookedRecipesBtn.addEventListener('click', async () => {
        cookedRecipesContent.classList.add('active');
        allergensContent.classList.remove('active');
        showCookedRecipesBtn.classList.add('active');
        showAllergensBtn.classList.remove('active');
        await loadUserCookedRecipes();
    });
}

if (showAddAllergenFormBtn) {
    showAddAllergenFormBtn.addEventListener('click', () => {
        if (addAllergenForm) addAllergenForm.hidden = false;
        showAddAllergenFormBtn.hidden = true;
    });
}

if (cancelNewAllergenBtn) {
    cancelNewAllergenBtn.addEventListener('click', () => {
        if (addAllergenForm) {
            addAllergenForm.hidden = true;
            if (newAllergenNameInput) newAllergenNameInput.value = '';
        }
        if (showAddAllergenFormBtn) showAddAllergenFormBtn.hidden = false;
    });
}

if (saveNewAllergenBtn) {
    saveNewAllergenBtn.addEventListener('click', async () => {
        const allergenName = newAllergenNameInput ? newAllergenNameInput.value.trim() : "";
        if (allergenName !== "") {
            await saveAllergenDB(allergenName);
            if (addAllergenForm) addAllergenForm.hidden = true;
            if (showAddAllergenFormBtn) showAddAllergenFormBtn.hidden = false;
            if (newAllergenNameInput) newAllergenNameInput.value = '';
        } else {
            alert("Название продукта не может быть пустым.");
        }
    });
}

onAuthStateChanged(auth, async (user) => {
    if (user) {
        if (allergensContent) allergensContent.classList.add('active');
        if (cookedRecipesContent) cookedRecipesContent.classList.remove('active');
        if (showAllergensBtn) showAllergensBtn.classList.add('active');
        if (showCookedRecipesBtn) showCookedRecipesBtn.classList.remove('active');
        await loadUserAllergens();
    } else {
        if (allergenListDiv) allergenListDiv.innerHTML = '<p><em>Пожалуйста, войдите в систему для просмотра продуктов.</em></p>';
        if (cookedRecipesListDiv) cookedRecipesListDiv.innerHTML = '<p><em>Пожалуйста, войдите в систему для просмотра приготовленных рецептов.</em></p>';
    }
});