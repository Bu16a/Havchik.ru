import { getAllProducts } from "./myproducts.js";
import { getAllergens } from "./profile.js";
import { onAuthStateChanged } from "https://www.gstatic.com/firebasejs/9.22.2/firebase-auth.js";
import { auth } from "./checkAuth.js";

const sortButton = document.getElementById("sortButton");
const sortMenu = document.getElementById("sortMenu");
const sortLabel = document.getElementById("sortLabel");
const options = document.querySelectorAll(".sort-option");
const sortIcon = document.getElementById("sortIcon");
const loader = document.getElementById("loader");
let isEnd = false;
let isLoading = false;
let page = 1;
let lastFetchController = null;
let isChecked = document.getElementById("add-products").checked;
let sortBy = document
    .getElementsByClassName("sort-option active")[0]
    .getAttribute("data-sort");
const token = getCookie("firebase_token");
export const apiUrl = "http://158.160.94.254:5252";

if ("serviceWorker" in navigator) {
    window.addEventListener("load", async () => {
        await navigator.serviceWorker.register(
            "/assets/code/service-worker.js",
        );
    });
}

function saveRecipesToCache(key, data) {
    sessionStorage.setItem(key, JSON.stringify(data));
}

function getRecipesFromCache(key) {
    const raw = sessionStorage.getItem(key);
    return raw ? JSON.parse(raw) : null;
}

function getCacheKey(isPurchase, sortBy, page, ingredients, allergens) {
    const ingredientsHash = ingredients.sort().join(",");
    const allergensHash = allergens.sort().join(",");
    return `recipes_${isPurchase}_${sortBy}_${page}_${ingredientsHash}_${allergensHash}`;
}

window.addEventListener("pageshow", async function (event) {
    isChecked = document.getElementById("add-products").checked;
    isEnd = false;
    if (event.persisted) {
        const products = Object.keys(await getAllProducts());
        const ingredientsToSend =
            Array.isArray(products) && products.length > 0 ? products : [];
        const allergensToSend = [];
        const allergens = await getAllergens();
        allergens.forEach((product) => {
            allergensToSend.push(product.name);
        });

        const cacheKey = getCacheKey(
            isChecked,
            sortBy,
            1,
            ingredientsToSend,
            allergensToSend,
        );
        const cached = getRecipesFromCache(cacheKey);

        if (cached) {
            const recipeCardsContainer =
                document.querySelectorAll(".recipe-cards")[0];
            recipeCardsContainer.innerHTML = "";
            createRecipeCards(cached);
        } else {
            showSkeletons();
            const recipes = await getShortRecipes(isChecked, sortBy);
            const recipeCardsContainer =
                document.querySelectorAll(".recipe-cards")[0];
            recipeCardsContainer.innerHTML = "";
            createRecipeCards(recipes);
        }
    }
});

function getCookie(name) {
    const cookies = document.cookie.split("; ");
    for (const cookie of cookies) {
        const [cookieName, cookieValue] = cookie.split("=");
        if (cookieName === name) return cookieValue;
    }
    return null;
}

async function getShortRecipes(isPurchase, sortBy, page = 1) {
    if (lastFetchController) {
        lastFetchController.abort();
    }
    lastFetchController = new AbortController();
    const { signal } = lastFetchController;

    try {
        isLoading = true;
        const products = Object.keys(await getAllProducts());
        const ingredientsToSend =
            Array.isArray(products) && products.length > 0 ? products : [];
        const allergensToSend = [];
        const allergens = await getAllergens();
        allergens.forEach((product) => {
            allergensToSend.push(product.name);
        });

        const cacheKey = getCacheKey(
            isPurchase,
            sortBy,
            page,
            ingredientsToSend,
            allergensToSend,
        );
        const cachedData = getRecipesFromCache(cacheKey);
        if (cachedData) {
            return cachedData;
        }

        const response = await fetch(`${apiUrl}/getRecipesBuyOrNo/data`, {
            method: "POST",
            headers: {
                "Content-Type": "application/json",
            },
            body: JSON.stringify({
                ingredients: ingredientsToSend,
                allergens: allergensToSend,
                count: 10,
                purchase: isPurchase,
                sortBy: sortBy,
                page: page,
            }),
            signal,
        });

        if (!response.ok) {
            throw new Error(`HTTP error! Status: ${response.status}`);
        }

        const result = await response.json();
        saveRecipesToCache(cacheKey, result);
        return result;
    } catch (error) {
        return null;
    } finally {
        isLoading = false;
    }
}

function createRecipeCards(recipesJson) {
    if (!recipesJson || typeof recipesJson !== "object") return;
    if (Object.keys(recipesJson).length === 0) {
        const recipeCardsContainer =
            document.querySelectorAll(".recipe-cards")[0];
        recipeCardsContainer.innerHTML =
            "<p>Рецептов не найдено :(<br>Добавьте побольше продуктов и попробуйте ещё раз</p>";
        console.warn("Нет данных для создания карточек рецептов.");
        return;
    }

    const keys = Object.keys(recipesJson);
    keys.forEach((key) => {
        if (key.startsWith("recipe_")) {
            const recipe = recipesJson[key];

            if (recipe !== null && recipe !== undefined) {
                const recipeCardInner = document.createElement("div");
                recipeCardInner.classList.add("recipe-card");

                const recipeText = document.createElement("div");
                recipeText.classList.add("recipe-text");

                const recipeTextDiv = document.createElement("div");

                const recipeTextH2 = document.createElement("h2");
                recipeTextH2.innerHTML = recipe.title || "Без названия";
                recipeTextDiv.appendChild(recipeTextH2);

                const recipeTextP = document.createElement("p");
                if (recipe.time)
                    recipeTextP.innerHTML = `Готовится ${recipe.time} мин`;
                else recipeTextP.innerHTML = "Время готовки неизвестно";
                recipeTextDiv.appendChild(recipeTextP);

                recipeText.appendChild(recipeTextDiv);

                const calories = document.createElement("p");
                let bgu = ["-", "-", "-", "-"];
                if (recipe.energy) bgu = recipe.energy;

                calories.innerHTML = `${bgu[0] || "0"} кКал<br>Б: ${bgu[1] || "0"} г<br>Ж: ${bgu[2] || "0"} г<br>У: ${bgu[3] || "0"} г`;
                recipeText.appendChild(calories);
                recipeCardInner.appendChild(recipeText);

                const recipeCardContainer = document.createElement("div");
                recipeCardContainer.className = "recipe-card-container";
                recipeCardContainer.onclick = function () {
                    location.href = `recipe.html?id=${recipe.id}`;
                };

                recipeCardInner.style.background = `linear-gradient(90deg, rgba(230, 230, 230, 0.6) 0%, rgba(230, 230, 230, 0.6) 50%, rgba(230, 230, 230, 0.6) 100%), url('${recipe.image}')`;
                recipeCardInner.style.backgroundSize = "cover";
                recipeCardInner.style.backgroundPosition = "center";

                recipeCardContainer.appendChild(recipeCardInner);
                const recipeCardsContainer =
                    document.querySelectorAll(".recipe-cards")[0];
                if (recipeCardsContainer) {
                    recipeCardsContainer.appendChild(recipeCardContainer);
                } else {
                    console.error(
                        "Элемент с классом 'recipe-cards' не найден.",
                    );
                }
            }
        }
    });
}

function showSkeletons(count = 5) {
    const container = document.querySelector(".recipe-cards");
    container.innerHTML = "";
    for (let i = 0; i < count; i++) {
        const skeleton = document.createElement("div");
        skeleton.classList.add("skeleton-card");
        container.appendChild(skeleton);
    }
}

sortButton.addEventListener("click", (e) => {
    e.stopPropagation();
    const isVisible = sortMenu.classList.contains("open");
    sortMenu.classList.toggle("open", !isVisible);
    sortIcon.classList.toggle("rotated", !isVisible);
});

options.forEach((option) => {
    if (option.classList.contains("active")) {
        sortLabel.textContent = option.textContent;
    }

    option.addEventListener("click", async () => {
        options.forEach((o) => o.classList.remove("active"));
        option.classList.add("active");
        sortLabel.textContent = option.textContent;

        sortBy = option.getAttribute("data-sort");
        const recipeCardsContainer =
            document.querySelectorAll(".recipe-cards")[0];
        if (recipeCardsContainer) {
            page = 1;
            showSkeletons();
        }

        sortMenu.classList.remove("open");
        sortIcon.classList.remove("rotated");

        isEnd = false;
        const recipes = await getShortRecipes(isChecked, sortBy);
        recipeCardsContainer.innerHTML = "";
        createRecipeCards(recipes);
    });
});

document.addEventListener("click", (e) => {
    if (!e.target.closest(".sort-wrapper")) {
        sortMenu.classList.remove("open");
        sortIcon.classList.remove("rotated");
    }
});

document
    .getElementById("add-products")
    .addEventListener("change", async (e) => {
        isChecked = e.target.checked;
        const recipeCardsContainer =
            document.querySelectorAll(".recipe-cards")[0];
        if (recipeCardsContainer) {
            page = 1;
            showSkeletons();
        }
        isEnd = false;
        const recipes = await getShortRecipes(isChecked, sortBy);
        recipeCardsContainer.innerHTML = "";
        createRecipeCards(recipes);
    });

onAuthStateChanged(auth, async (user) => {
    if (user) {
        showSkeletons();
        const recipes = await getShortRecipes(isChecked, sortBy);

        const recipeCardsContainer =
            document.querySelectorAll(".recipe-cards")[0];
        if (recipeCardsContainer) {
            page = 1;
        }
        isEnd = false;
        recipeCardsContainer.innerHTML = "";
        createRecipeCards(recipes);
    } else {
        const recipeCardsContainer =
            document.querySelectorAll(".recipe-cards")[0];
        if (recipeCardsContainer) {
            recipeCardsContainer.innerHTML = "";
        }
    }
});

window.addEventListener("scroll", async () => {
    const { scrollTop, scrollHeight, clientHeight } = document.documentElement;
    if (
        scrollTop + clientHeight >= scrollHeight - 100 &&
        !isLoading &&
        !isEnd
    ) {
        loader.style.display = "flex";
        const recipes = await getShortRecipes(isChecked, sortBy, ++page);
        loader.style.display = "none";
        if (!recipes || Object.keys(recipes).length === 0) {
            isEnd = true;
            return;
        }
        if (Object.keys(recipes).length > 0) createRecipeCards(recipes);
    }
});
