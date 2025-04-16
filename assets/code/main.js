const sortButton = document.getElementById('sortButton');
const sortMenu = document.getElementById('sortMenu');
const sortLabel = document.getElementById('sortLabel');
const options = document.querySelectorAll('.sort-option');
const sortIcon = document.getElementById('sortIcon');

const recipeCards = document.querySelectorAll('.recipe-card');

recipeCards.forEach(card => {
    const imageUrl = card.getAttribute('data-image-url');
    card.style.background = `linear-gradient(90deg, rgba(217, 217, 217, 0.8) 0%, rgba(217, 217, 217, 0.6) 50%, rgba(217, 217, 217, 0.8) 100%), url('${imageUrl}')`;
    card.style.backgroundSize = 'cover';
    card.style.backgroundPosition = 'center';
});


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
    // TODO: обработать докуп продуктов
});




