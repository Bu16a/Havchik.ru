import { initializeApp } from 'https://www.gstatic.com/firebasejs/9.22.2/firebase-app.js';
import { getAuth, createUserWithEmailAndPassword, signInWithEmailAndPassword } from 'https://www.gstatic.com/firebasejs/9.22.2/firebase-auth.js';
import { getFirestore, doc, setDoc } from "https://www.gstatic.com/firebasejs/9.22.2/firebase-firestore.js";

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

function setCookie(name, value, minutesToExpire) {
    const date = new Date();
    date.setTime(date.getTime() + (minutesToExpire * 60 * 1000));
    const expires = "expires=" + date.toUTCString();
    document.cookie = `${name}=${value}; ${expires}; path=/; Secure; SameSite=Lax`;
}

function singUpSuccess(userCredential) {
    alert("Регистрация успешна!");
    const user = userCredential.user;
    createMyProducts(user.uid)
    .then(() => {
        console.log("Документ создан");
        return user.getIdToken();
    })
    .then((idToken) => {
        setCookie("firebase_token", idToken, 60);
        console.log("User created and token obtained:", user);
        window.location.href = '/index.html';
    })
    .catch((error) => {
        console.error("Ошибка при получения токена или создания документа:", error);
    });
}

async function createMyProducts(id) {
    const userDocRef = doc(db, "users", id); // users/{uid}
    await setDoc(userDocRef, {
        products: {
            Помидор : "3 шт",
            Молоко : "500 гр",
            Лапша : "500 гр"
        },
        cooked: [],
        allergens: []
    })
    .then(() => console.log("1"));
}

function signUpFaile(error) {
    console.error("Ошибка регистрации:", error.code, error.message);
    let errorMessage = "Ошибка регистрации: ";
    switch(error.code) {
        case 'auth/email-already-in-use':
            errorMessage += "Этот email уже зарегистрирован";
            break;
        case 'auth/invalid-email':
            errorMessage += "Некорректный email";
            break;
        case 'auth/weak-password':
            errorMessage += "Пароль должен содержать минимум 6 символов";
            break;
        default:
            errorMessage += error.message;
    }
    alert(errorMessage);
}

function signInSuccess(userCredential) {
    const user = userCredential.user;
    user.getIdToken()
        .then((idToken) => {
            setCookie("firebase_token", idToken, 60);
            window.location.href = '/index.html';
        })
        .catch((error) => {
            console.error("Ошибка получения токена:", error);
        });
    alert("Успешный вход");
    console.log("User зашел:", user);
}

function signInFail(error) {
    console.error("Ошибка входа:", error.code, error.message);
    let errorMessage = "Ошибка входа: ";
    switch(error.code) {
        case 'auth/user-not-found':
            errorMessage += "Пользователь не найден";
            break;
        case 'auth/invalid-email':
            errorMessage += "Некорректный email";
            break;
        case 'auth/wrong-password':
            errorMessage += "Пароль неверный";
            break;
        case 'auth/invalid-login-credentials':
            errorMessage += "Неверный пароль или почта";
            break
        default:
            errorMessage += error.message;
    }
    alert(errorMessage);
}

document.addEventListener('DOMContentLoaded', function() {
    const signUpForm = document.querySelector('.sign-up-container .auth-form form');
    const signInForm = document.querySelector('.sign-in-container .auth-form form');

    document.getElementById("signUpBtt").addEventListener('click', function(e) {
        const signUp = document.querySelector(".sign-up-container");
        const signIn = document.querySelector(".sign-in-container");
        signUp.style.display = "flex";
        signIn.style.display = "none";
    })
    
    document.getElementById("signInBtt").addEventListener('click', function(e) {
        const signUp = document.querySelector(".sign-up-container");
        const signIn = document.querySelector(".sign-in-container");
        signIn.style.display = "flex";
        signUp.style.display = "none";
    })
    
    if (signUpForm) {
        signUpForm.addEventListener('submit', function(e) {
            e.preventDefault();
            const email = document.getElementById('email1').value;
            const password = document.getElementById('password1').value;
            createUserWithEmailAndPassword(auth, email, password)
            .then(singUpSuccess)
            .catch(signUpFaile);
        });
    }

    if (signInForm) {
        signInForm.addEventListener('submit', function(e) {
            e.preventDefault();
            const email = document.getElementById('email2').value;
            const password = document.getElementById('password2').value;
            signInWithEmailAndPassword(auth, email, password)
            .then(signInSuccess)
            .catch(signInFail);
        });
    }
});