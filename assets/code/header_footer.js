class SiteHeader extends HTMLElement {
    connectedCallback() {
        this.innerHTML = `
            <header>
                <a href="/index.html" class="logo" aria-label="Главная"></a>
                <nav class="pages">
                    <a href="/myproducts.html" class="profile" aria-label="Продукты">
                         <img src="/assets/svg/fridge.svg">Холодильник
                    </a>
                    <a href="/profile.html" class="profile" aria-label="Профиль">
                        <img src="/assets/svg/profile.svg">Профиль
                    </a>
                </nav>
            </header>
        `;
    }
}

class SiteFooter extends HTMLElement {
    connectedCallback() {
        this.innerHTML = `
            <footer>
                <p>
                    Скучаю, но верстаю <3 от <a href="https://github.com/Bu16a/Havchik.ru" class="github" aria-label="Хавчик.ру">Хавчика</a>
                </p>
            </footer>
        `;
    }
}

customElements.define("site-header", SiteHeader);
customElements.define("site-footer", SiteFooter);
