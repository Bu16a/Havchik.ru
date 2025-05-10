import {getAuth, signOut, onAuthStateChanged} from "https://www.gstatic.com/firebasejs/9.22.2/firebase-auth.js";
import {initializeApp} from 'https://www.gstatic.com/firebasejs/9.22.2/firebase-app.js';
import {
    getFirestore,
    doc,
    updateDoc,
    arrayUnion,
    getDoc,
    arrayRemove
} from "https://www.gstatic.com/firebasejs/9.22.2/firebase-firestore.js";

const firebaseConfig = {
    apiKey: "AIzaSyArIiiX0vU-_Kr_CJRLdtIs5qTHIUTvUc8",
    authDomain: "che-te.firebaseapp.com",
    projectId: "che-te",
    storageBucket: "che-te.appspot.com",
    messagingSenderId: "902131293726",
    appId: "1:902131293726:web:4a8a3aff1cf0c9d4e1180f",
    measurementId: "G-BXT01SDXW2"
};

const app = initializeApp(firebaseConfig);
const auth = getAuth(app);
const db = getFirestore(app);

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
function generateAllergenId() {
    return Date.now().toString(36) + Math.random().toString(36).substring(2);
}

async function saveAllergenDB(allergenName) {
    if (!auth.currentUser) {
        console.error("User not authenticated to save allergen.");
        alert("Пожалуйста, войдите в систему, чтобы сохранить аллерген.");
        return;
    }
    const userDocRef = doc(db, "users", auth.currentUser.uid);
    const allergenId = generateAllergenId(); // Generate an ID for the allergen
    try {
        await updateDoc(userDocRef, {
            allergens: arrayUnion({name: allergenName, id: allergenId})
        });
        console.log("Allergen saved:", allergenName);
        await loadUserAllergens(); // Refresh the list
    } catch (error) {
        console.error("Error saving allergen:", error);
        alert("Ошибка при сохранении аллергена.");
    }
}

export async function getAllergens() {
    if (!auth.currentUser) {
        console.log("User not authenticated to get allergens.");
        return [];
    }
    const userDocRef = doc(db, "users", auth.currentUser.uid);
    try {
        const docSnap = await getDoc(userDocRef);
        if (docSnap.exists() && docSnap.data().allergens) {
            return docSnap.data().allergens;
        } else {
            console.log("No allergens found for this user or user document doesn't exist.");
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
        alert("Пожалуйста, войдите в систему, чтобы удалить аллерген.");
        return;
    }
    const userDocRef = doc(db, "users", auth.currentUser.uid);
    try {
        await updateDoc(userDocRef, {
            allergens: arrayRemove(allergen)
        });
        console.log("Allergen removed:", allergen.name);
        await loadUserAllergens();
    } catch (error) {
        console.error("Error removing allergen:", error);
        alert("Ошибка при удалении аллергена.");
    }
}

function displayAllergens(allergens) {
    if (!allergenListDiv) return;
    allergenListDiv.innerHTML = '';

    if (!allergens || allergens.length === 0) {
        allergenListDiv.innerHTML = '<p><em>У вас пока нет добавленных аллергенов.</em></p>';
        return;
    }

    const ul = document.createElement('ul');
    ul.classList.add('profile-item-list'); // Add a class for styling if needed
    allergens.forEach(allergen => {
        const li = document.createElement('li');
        li.classList.add('profile-item'); // Add a class for styling
        li.textContent = allergen.name;

        const deleteBtn = document.createElement('button');
        const img = document.createElement("img");
        img.src = "assets/svg/trash.svg";
        img.alt = "Удалить";
        deleteBtn.appendChild(img);
        deleteBtn.classList.add('delete-item-btn'); // Add a class for styling
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

// --- Cooked Recipes Functions ---
async function getCookedRecipes() {
    if (!auth.currentUser) {
        console.log("User not authenticated to get cooked recipes.");
        return [];
    }
    const userDocRef = doc(db, "users", auth.currentUser.uid);
    try {
        const docSnap = await getDoc(userDocRef);
        if (docSnap.exists() && docSnap.data().cooked) {
            return docSnap.data().cooked; // Array of {name: string, id: number}
        } else {
            console.log("No cooked recipes found for this user or user document doesn't exist.");
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
            console.log("Пользователь вышел из системы");
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
        await loadUserAllergens(); // Load allergens when tab is clicked
    });
}

if (showCookedRecipesBtn) {
    showCookedRecipesBtn.addEventListener('click', async () => {
        cookedRecipesContent.classList.add('active');
        allergensContent.classList.remove('active');
        showCookedRecipesBtn.classList.add('active');
        showAllergensBtn.classList.remove('active');
        await loadUserCookedRecipes(); // Load recipes when tab is clicked
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
            alert("Название аллергена не может быть пустым.");
        }
    });
}

onAuthStateChanged(auth, async (user) => {
    if (user) {
        console.log("User is signed in, loading profile data...");
        if (allergensContent) allergensContent.classList.add('active');
        if (cookedRecipesContent) cookedRecipesContent.classList.remove('active');
        if (showAllergensBtn) showAllergensBtn.classList.add('active');
        if (showCookedRecipesBtn) showCookedRecipesBtn.classList.remove('active');
        await loadUserAllergens();
    } else {
        console.log("User is signed out.");
        if (allergenListDiv) allergenListDiv.innerHTML = '<p><em>Пожалуйста, войдите в систему для просмотра аллергенов.</em></p>';
        if (cookedRecipesListDiv) cookedRecipesListDiv.innerHTML = '<p><em>Пожалуйста, войдите в систему для просмотра приготовленных рецептов.</em></p>';
    }
});