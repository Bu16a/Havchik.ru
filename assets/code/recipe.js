import {auth, db} from "./checkAuth.js";
import {getAllProducts} from './myproducts.js';
import {onAuthStateChanged} from 'https://www.gstatic.com/firebasejs/9.22.2/firebase-auth.js';
import {doc, updateDoc, arrayUnion, getDoc} from "https://www.gstatic.com/firebasejs/9.22.2/firebase-firestore.js";

const apiUrl = 'http://158.160.94.254:5252';
const url = new URL(window.location.href);
const params = new URLSearchParams(url.search);
const recipeId = Number(params.get('id'));

async function getRecipe() {
    try {
        const response = await fetch(`${apiUrl}/searchRecipebyid/data`, {
            method: 'POST',
            headers: {
                'Content-Type': 'application/json'
            },
            body: JSON.stringify({
                id: recipeId
            })
        });
        if (!response.ok) {
            throw new Error(`HTTP error! Status: ${response.status}`);
        }
        return await response.json();

    } catch (error) {
        console.error(error);
        return null;
    }
}

async function printRecipe(recipeJson) {
    if (!recipeJson || typeof recipeJson !== 'object') {
        console.warn("Нет данных для создания рецепта.");
        return;
    }

    // Создаём recipe-header
    const recipeHeader = document.createElement('div');
    recipeHeader.classList.add('recipe-header');

    const recipeImage = document.createElement('img');
    recipeImage.classList.add('recipe-image');
    recipeImage.src = recipeJson.image;
    recipeHeader.appendChild(recipeImage);

    // Создаём recipe-title
    const recipeTitle = document.createElement('div');
    recipeTitle.classList.add('recipe-title');

    const recipeName = document.createElement('h1');
    recipeName.innerHTML = recipeJson.title;

    const recipeTime = document.createElement('div');
    recipeTime.classList.add('time');
    recipeTime.innerHTML = recipeJson.time
        ? `<p>Готовится ${recipeJson.time} мин</p>`
        : 'Время готовки неизвестно';

    recipeTitle.appendChild(recipeName);
    recipeTitle.appendChild(recipeTime);

    // Создаём recipe-ingredients
    const recipeIngredients = document.createElement('div');
    recipeIngredients.classList.add('recipe-ingredients');

    const ingredientsTitle = document.createElement('h1');
    ingredientsTitle.classList.add('section-title');
    ingredientsTitle.innerHTML = 'Продукты';

    const ingredientList = document.createElement('div');
    ingredientList.classList.add('recipe-list');
    const ingredientsData = JSON.parse(recipeJson.ingredients);
    const products = await getAllProducts();
    if (ingredientsData)
        Object.keys(ingredientsData).forEach(ingredient => {
            const ingredientItem = document.createElement('div');
            ingredientItem.classList.add('recipe-item');
            let hasProduct = false;
            Object.keys(products).forEach((product) => {

                const productLower = product.toLowerCase();
                const ingredientLower = ingredient.toLowerCase();
                console.log(`${productLower} ${ingredientLower}`);
                if (productLower.indexOf(ingredientLower) !== -1 ||
                    ingredientLower.indexOf(productLower) !== -1) {
                    hasProduct = true;
                    return;
                }
            });
            ingredientItem.style.background = hasProduct
                ? `linear-gradient(90deg, rgba(230, 230, 230, 0.8) 0%, rgba(230, 230, 230, 0.6) 50%, rgba(230, 230, 230, 0.8) 100%)`
                : `linear-gradient(90deg, rgba(255, 130, 130, 0.8) 0%, rgba(255, 130, 130, 0.6) 50%, rgba(255, 130, 130, 0.8) 100%)`;
            const ingredientName = document.createElement('span');
            ingredientName.classList.add('ingredient-name');
            ingredientName.innerHTML = ingredient;

            const ingredientAmount = document.createElement('span');
            if (ingredientsData[ingredient])
                ingredientAmount.innerHTML = ingredientsData[ingredient];
            ingredientAmount.classList.add('ingredient-amount');

            ingredientItem.appendChild(ingredientName);
            ingredientItem.appendChild(ingredientAmount);
            ingredientList.appendChild(ingredientItem);
        })

    recipeIngredients.appendChild(ingredientsTitle);
    recipeIngredients.appendChild(ingredientList);

    //Создаём recipe-directions
    const recipeDirections = document.createElement('div');
    recipeDirections.classList.add('recipe-directions');

    const recipeDirectionsTitle = document.createElement('h1');
    recipeDirectionsTitle.classList.add('section-title');
    recipeDirectionsTitle.innerHTML = 'Рецепт';

    const recipeDirectionsList = document.createElement('div');
    recipeDirectionsList.classList.add('recipe-list');

    recipeJson.directions.forEach((direction, index) => {
        const directionItem = document.createElement('div');
        directionItem.classList.add('recipe-item');

        const directionIndex = document.createElement('span');
        directionIndex.classList.add('tutorial-step');
        directionIndex.innerHTML = `Шаг ${index + 1}`;

        const step = document.createElement('span');
        step.classList.add('step-desc');
        step.innerHTML = direction;

        directionItem.appendChild(directionIndex);
        directionItem.appendChild(step);
        recipeDirectionsList.appendChild(directionItem);
    })

    recipeDirections.appendChild(recipeDirectionsTitle);
    recipeDirections.appendChild(recipeDirectionsList);

    // Создаём button
    const readyButton = document.createElement('button');
    readyButton.classList.add('ready-btn');
    readyButton.type = 'submit';
    readyButton.innerHTML = '<strong>Готово!</strong>';
    readyButton.addEventListener('click', (event) => {
        saveRecipeDB(recipeJson.title)
    })

    const recipeSource = document.createElement('div');
    recipeSource.classList.add('source');
    recipeSource.style.color = '#D9D9D9';
    recipeSource.style.outline = 'none';
    recipeSource.style.textDecoration = 'none';
    recipeSource.innerHTML = `<a href="https://www.povarenok.ru/recipes/show/${recipeJson.orig_id}" style="color: #666; outline: none; text-decoration: none;">Источник рецепта: povarenok.ru</a>`;

    // Создаём recipe-content
    const recipeContent = document.createElement('div');
    recipeContent.classList.add('recipe-content');
    recipeContent.appendChild(recipeTitle);
    recipeContent.appendChild(recipeIngredients);
    recipeContent.appendChild(recipeDirections);
    recipeContent.appendChild(recipeSource);
    recipeContent.appendChild(readyButton);


    const container = document.querySelector('.recipe-container');
    container.appendChild(recipeHeader);
    container.appendChild(recipeContent);
}

async function saveRecipeDB(title) {
    const userDocRef = doc(db, "users", auth.currentUser.uid);
    await updateDoc(userDocRef, {
        cooked: arrayUnion({name: title, id: recipeId})
    })
}

async function getCookedRecipes() {
    const userDocRef = doc(db, "users", auth.currentUser.uid);
    const data = await getDoc(userDocRef);
    if (data.exists()) {
        return data.data().cooked; //массив, данные так достаются: cooked[0].id cooked[0].name
        //console.log(`${data.data().cooked[0].id} ${data.data().cooked[0].name}`);
    } else {
        console.log("А ГДЕ")
    }
}

onAuthStateChanged(auth, async (user) => {
    if (user) {
        const recipe = await getRecipe();
        console.log(recipe);
        if (recipe) {
            const recipeContainer = document.querySelectorAll('.recipe-container')[0];
            if (recipeContainer) {
                recipeContainer.innerHTML = '';
            }
            printRecipe(recipe);
        } else {
            console.log("Не удалось получить рецепт после успешной авторизации.");
        }
    } else {
        console.log('Пользователь не авторизован');
        const recipeCardsContainer = document.querySelectorAll('.recipe-container')[0];
        if (recipeCardsContainer) {
            recipeCardsContainer.innerHTML = '';
        }
    }
});