import {initializeApp} from 'https://www.gstatic.com/firebasejs/9.22.2/firebase-app.js';
import {getAuth, onAuthStateChanged} from 'https://www.gstatic.com/firebasejs/9.22.2/firebase-auth.js';

const firebaseConfig = {
    apiKey: "AIzaSyArIiiX0vU-_Kr_CJRLdtIs5qTHIUTvUc8",
    authDomain: "che-te.firebaseapp.com",
    projectId: "che-te",
    storageBucket: "che-te.appspot.com",
    messagingSenderId: "902131293726",
    appId: "1:902131293726:web:4a8a3aff1cf0c9d4e1180f",
    measurementId: "G-BXT01SDXW2"
};

export const app = initializeApp(firebaseConfig);
export const auth = getAuth(app);

onAuthStateChanged(auth, (user) => {
    if (user) {
        console.log("Пользователь авторизован:", user);
        document.body.style.display = "block";
    } else {
        console.log("Пользователь не авторизован");
        window.location.href = '/auth.html';
    }
});