const CACHE_NAME = "my-pwa-v1";
const urlsToCache = [
    "/",
    "/index.html",
    "/auth.html",
    "/myproducts.html",
    "/profile.html",
    "/recipe.html",
    "/manifest.json",
    "/assets/style/main.css",
    "/assets/style/styles.css",
    "/assets/style/profile.css",
    "/assets/style/recipe.css",
    "/assets/style/myproducts.css",
    "/assets/style/registration.css",
    "/assets/code/main.js",
    "/assets/code/profile.js",
    "/assets/code/recipe.js",
    "/assets/code/registration.js",
    "/assets/code/myproducts.js",
    "/assets/code/header_footer.js",
    "/assets/code/checkAuth.js",
    "/assets/svg/logo.png",
    "/assets/svg/fridge.svg",
    "/assets/svg/profile.svg",
    "/assets/svg/save.svg",
    "/assets/svg/trash.svg",
    "/assets/svg/edit.svg",
    "/assets/svg/expand.svg",
    "/assets/svg/cancel.svg",
];

self.addEventListener("install", (event) => {
    event.waitUntil(
        caches.open(CACHE_NAME).then((cache) => cache.addAll(urlsToCache)),
    );
});

self.addEventListener("fetch", (event) => {
    event.respondWith(
        caches
            .match(event.request)
            .then((response) => response || fetch(event.request)),
    );
});
