import {getAllProducts} from './myproducts.js';
import {getAuth, onAuthStateChanged} from 'https://www.gstatic.com/firebasejs/9.22.2/firebase-auth.js';
import {app, auth} from "./checkAuth.js";

const sortButton = document.getElementById('sortButton');
const sortMenu = document.getElementById('sortMenu');
const sortLabel = document.getElementById('sortLabel');
const options = document.querySelectorAll('.sort-option');
const sortIcon = document.getElementById('sortIcon');
let isLoading = false;
let page = 1;
let isChecked = false;
let sortBy = document.getElementsByClassName('sort-option active')[0].getAttribute('data-sort');
const token = getCookie("firebase_token");
export const apiUrl = 'http://localhost:8080';

function getCookie(name) {
    const cookies = document.cookie.split('; ');
    for (const cookie of cookies) {
        const [cookieName, cookieValue] = cookie.split('=');
        if (cookieName === name) return cookieValue;
    }
    return null;
}

async function getShortRecipes(isPurchase = false, sortBy = 'relevance', page = 1) {
    try {
        console.log(isPurchase, sortBy, page);
        isLoading = true;
        const products = Object.keys(await getAllProducts());
        const ingredientsToSend = Array.isArray(products) && products.length > 0 ? products : [];

        const response = await fetch(`${apiUrl}/getRecipesBuyOrNo/data`, {
            method: 'POST',
            headers: {
                'Content-Type': 'application/json'
            },
            body: JSON.stringify({
                ingredients: ingredientsToSend,
                count: 10,
                purchase: isPurchase,
                sortBy: sortBy,
                page: page
            })
        });

        if (!response.ok) {
            throw new Error(`HTTP error! Status: ${response.status}`);
        }
        return await response.json();

    } catch (error) {
        return null;
    } finally {
        isLoading = false;
    }
}


function createRecipeCards(recipesJson) {
    if (!recipesJson || typeof recipesJson !== 'object') {
        console.warn("Нет данных для создания карточек рецептов.");
        return;
    }

    const keys = Object.keys(recipesJson);
    keys.forEach(key => {
        if (key.startsWith("recipe_")) {
            const recipe = recipesJson[key];

            if (recipe !== null && recipe !== undefined) {
                const recipeCardInner = document.createElement('div');
                recipeCardInner.classList.add('recipe-card');

                const recipeText = document.createElement('div');
                recipeText.classList.add('recipe-text');

                const recipeTextDiv = document.createElement('div');

                const recipeTextH2 = document.createElement('h2');
                recipeTextH2.innerHTML = recipe.title || 'Без названия';
                recipeTextDiv.appendChild(recipeTextH2);

                const recipeTextP = document.createElement('p');
                if (recipe.time)
                    recipeTextP.innerHTML = `Готовится ${recipe.time} мин`;
                else
                    recipeTextP.innerHTML = 'Время готовки неизвестно';
                recipeTextDiv.appendChild(recipeTextP);

                recipeText.appendChild(recipeTextDiv);

                const calories = document.createElement('p');
                let bgu = ['-', '-', '-', '-'];
                if (recipe.energy)
                    bgu = recipe.energy;

                calories.innerHTML = `${bgu[0] || '0'} кКал<br>Б: ${bgu[1] || '0'} г<br>Ж: ${bgu[2] || '0'} г<br>У: ${bgu[3] || '0'} г`;
                recipeText.appendChild(calories);
                recipeCardInner.appendChild(recipeText);

                const recipeCardContainer = document.createElement('div');
                recipeCardContainer.className = 'recipe-card-container';
                recipeCardContainer.onclick = function () {
                    location.href = `recipe.html?id=${recipe.id}`;
                    console.log(`Переход на страницу рецепта: ${recipe.title}`);
                }

                // recipeCardInner.style.background = `linear-gradient(90deg, rgba(217, 217, 217, 0.8) 0%, rgba(217, 217, 217, 0.6) 50%, rgba(217, 217, 217, 0.8) 100%), url('${'placeholder.jpg'}')`;
                recipeCardInner.style.backgroundSize = 'cover';
                recipeCardInner.style.backgroundPosition = 'center';

                recipeCardContainer.appendChild(recipeCardInner);
                const recipeCardsContainer = document.querySelectorAll('.recipe-cards')[0];
                if (recipeCardsContainer) {
                    recipeCardsContainer.appendChild(recipeCardContainer);
                } else {
                    console.error("Элемент с классом 'recipe-cards' не найден.");
                }
            }
        }
    });
}


sortButton.addEventListener('click', (e) => {
    e.stopPropagation();
    const isVisible = sortMenu.classList.contains('open');
    sortMenu.classList.toggle('open', !isVisible);
    sortIcon.classList.toggle('rotated', !isVisible);
});

options.forEach(option => {
    if (option.classList.contains('active')) {
        sortLabel.textContent = option.textContent;
    }

    option.addEventListener('click', async () => {
        options.forEach(o => o.classList.remove('active'));
        option.classList.add('active');
        sortLabel.textContent = option.textContent;

        sortBy = option.getAttribute('data-sort');
        const recipeCardsContainer = document.querySelectorAll('.recipe-cards')[0];
        if (recipeCardsContainer) {
            page = 1;
            recipeCardsContainer.innerHTML = '';
        }

        sortMenu.classList.remove('open');
        sortIcon.classList.remove('rotated');

        const recipes = await getShortRecipes(isChecked, sortBy);
        if (recipes) {
            createRecipeCards(recipes);
        } else {
            const recipeCardsContainer = document.querySelectorAll('.recipe-cards')[0];
            recipeCardsContainer.innerHTML = '<p>Рецептов не найдено :(<br>Добавьте побольше продуктов и попробуйте ещё раз</p>';
            console.log("Не удалось получить рецепты после изменения сортировки.");
        }
    });
});

document.addEventListener('click', (e) => {
    if (!e.target.closest('.sort-wrapper')) {
        sortMenu.classList.remove('open');
        sortIcon.classList.remove('rotated');
    }
});

document.getElementById('add-products').addEventListener('change', async (e) => {
    isChecked = e.target.checked;
    const recipeCardsContainer = document.querySelectorAll('.recipe-cards')[0];
    if (recipeCardsContainer) {
        page = 1;
        recipeCardsContainer.innerHTML = '';
    }

    const recipes = await getShortRecipes(isChecked, sortBy);
    if (recipes) {
        createRecipeCards(recipes);
    } else {
        const recipeCardsContainer = document.querySelectorAll('.recipe-cards')[0];
        recipeCardsContainer.innerHTML = '<p>Рецептов не найдено :(<br>Добавьте побольше продуктов и попробуйте ещё раз</p>';
        console.log("Не удалось получить рецепты после изменения чекбокса.");
    }
});

onAuthStateChanged(auth, async (user) => {
    if (user) {
        const recipes = await getShortRecipes();
        if (recipes) {
            const recipeCardsContainer = document.querySelectorAll('.recipe-cards')[0];
            if (recipeCardsContainer) {
                recipeCardsContainer.innerHTML = '';
                page = 1;
            }
            createRecipeCards(recipes);
        } else {
            const recipeCardsContainer = document.querySelectorAll('.recipe-cards')[0];
            recipeCardsContainer.innerHTML = '<p>Рецептов не найдено :(<br>Добавьте побольше продуктов и попробуйте ещё раз</p>';
            console.log("Не удалось получить рецепты после успешной авторизации.");
        }
    } else {
        console.log('Пользователь не авторизован');
        const recipeCardsContainer = document.querySelectorAll('.recipe-cards')[0];
        if (recipeCardsContainer) {
            recipeCardsContainer.innerHTML = '';
        }
    }
});

window.addEventListener('scroll', async () => {
    const {scrollTop, scrollHeight, clientHeight} = document.documentElement;
    if (scrollTop + clientHeight >= scrollHeight - 100 && !isLoading) {
        const recipes = await getShortRecipes(isChecked, sortBy, ++page);
        if (recipes) {
            createRecipeCards(recipes);
        } else {
            const recipeCardsContainer = document.querySelectorAll('.recipe-cards')[0];
            recipeCardsContainer.innerHTML = '<p>Рецептов не найдено :(<br>Добавьте побольше продуктов и попробуйте ещё раз</p>';
            console.log("Не удалось получить рецепты после изменения сортировки.");
        }
    }
});

