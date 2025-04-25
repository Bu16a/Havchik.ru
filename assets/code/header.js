class SiteHeader extends HTMLElement {
    connectedCallback() {
        this.innerHTML = `
            <header>
                <a href="/index.html" class="logo" aria-label="Главная"></a>
                <nav class="pages">
                    <a href="/myproducts.html" class="profile" aria-label="Продукты">
                         <img src="/assets/svg/fridge.svg">Холодильник
                    </a>
                    <a href="/auth.html" class="profile" aria-label="Профиль">
                        <img src="/assets/svg/profile.svg">Профиль
                    </a>
                </nav>
            </header>
        `;
    }
}

customElements.define('site-header', SiteHeader);
