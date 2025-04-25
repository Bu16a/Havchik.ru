import { getAuth, signOut } from "https://www.gstatic.com/firebasejs/9.22.2/firebase-auth.js";
import { initializeApp } from 'https://www.gstatic.com/firebasejs/9.22.2/firebase-app.js';

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

document.getElementById("logOut").addEventListener('click', () => {
    signOut(auth).then(() => {
        console.log("Пользователь вышел из системы");
        window.location.href = '/auth.html';
    }).catch((error) => {
        console.error("Ошибка выхода:", error);
    });
});