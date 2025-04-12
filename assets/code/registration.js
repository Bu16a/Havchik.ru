document.addEventListener('DOMContentLoaded', function() {
    const registrationForm = document.querySelector('.auth-form form');
    
    registrationForm.addEventListener('submit', function(e) {
        e.preventDefault();
        
        const email = document.getElementById('email').value;
        const password = document.getElementById('password').value;
        
        const userData = {
            email: email,
            password: password
        };
        
        fetch('https://your-server-url.com/api/register', {
            method: 'POST',
            headers: {
                'Content-Type': 'application/json',
            },
            body: JSON.stringify(userData)
        })
        .then(response => {
            if (!response.ok) {
                throw new Error('Ошибка сети');
            }
            return response.json();
        })
        .then(data => {
            console.log('Успешная регистрация:', data);
            alert('Регистрация прошла успешно!');
        })
        .catch(error => {
            console.error('Ошибка:', error);
            alert('Произошла ошибка при регистрации: ' + error.message);
        });
    });
});