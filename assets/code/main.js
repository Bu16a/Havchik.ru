const sortButton = document.getElementById('sortButton');
const sortMenu = document.getElementById('sortMenu');
const sortLabel = document.getElementById('sortLabel');
const options = document.querySelectorAll('.sort-option');
const sortIcon = document.getElementById('sortIcon');
const token = getCookie("firebase_token");
const apiUrl = 'https://8214-94-228-163-230.ngrok-free.app';

function getCookie(name) {
    const cookies = document.cookie.split('; ');
    for (const cookie of cookies) {
        const [cookieName, cookieValue] = cookie.split('=');
        if (cookieName === name) return cookieValue;
    }
    return null;
}

async function getShortRecipes(isPurchase = false) {
    try {
        const response = await fetch(`${apiUrl}/getRecipesBuyOrNo/data`, {
            method: 'POST',
            headers: {
                'Content-Type': 'application/json'
            },
            body: JSON.stringify({
                ingredients: ["хрен"],
                count: 10,
                purchase: isPurchase
            })
        });

        if (!response.ok) {
            throw new Error(`HTTP error! Status: ${response.status}`);
        }

        return await response.json();
    } catch (error) {
        console.error('Ошибка запроса:', error);
        return null;
    }
}


function createRecipeCards(recipesJson) {
    console.log(recipesJson);
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
                recipeTextH2.innerHTML = recipe.title;
                recipeTextDiv.appendChild(recipeTextH2);

                const recipeTextP = document.createElement('p');
                recipeTextP.innerHTML = recipe.title;
                recipeTextDiv.appendChild(recipeTextP);

                recipeText.appendChild(recipeTextDiv);

                const calories = document.createElement('p');
                calories.innerHTML = `кКал<br>Б: г<br>Ж: г<br>У: г`;
                recipeText.appendChild(calories);
                recipeCardInner.appendChild(recipeText);

                const recipeCard = document.createElement('div');
                recipeCard.className = 'recipe-card';
                recipeCard.onclick = function () {
                    location.href = 'recipe.html';
                }

                recipeCardInner.style.background = `linear-gradient(90deg, rgba(217, 217, 217, 0.8) 0%, rgba(217, 217, 217, 0.6) 50%, rgba(217, 217, 217, 0.8) 100%), url('${recipe.image}')`;
                recipeCardInner.style.backgroundSize = 'cover';
                recipeCardInner.style.backgroundPosition = 'center';
                recipeCard.appendChild(recipeCardInner);
                document.querySelectorAll('.recipe-cards')[0].appendChild(recipeCardInner);
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
    option.addEventListener('click', () => {
        options.forEach(o => o.classList.remove('active'));
        option.classList.add('active');
        sortLabel.textContent = option.textContent;
        sortMenu.classList.remove('open');
        sortIcon.classList.remove('rotated');
        // TODO: обработать сортировку
    });
});

document.addEventListener('click', (e) => {
    if (!e.target.closest('.sort-wrapper')) {
        sortMenu.classList.remove('open');
        sortIcon.classList.remove('rotated');
    }
});

document.getElementById('add-products').addEventListener('change', (e) => {
    const isChecked = e.target.checked;
    document.querySelectorAll('.recipe-cards')[0].innerHTML = '';
    getShortRecipes(isChecked).then(recipes => {
        createRecipeCards(recipes);
    });
});

const products = fetch(`${apiUrl}/products?token=${token}`);
getShortRecipes().then(recipes => {
    createRecipeCards(recipes);
});

