// CRIX SMP - Shop & Cart System

const PACKAGES = [
    { id: 'cb1', coins: 1000, priceUSD: 1.00, name: 'Starter Pack' },
    { id: 'cb2', coins: 5000, priceUSD: 4.50, name: 'Bronze Pack' },
    { id: 'cb3', coins: 10000, priceUSD: 8.00, name: 'Silver Pack' },
    { id: 'cb4', coins: 50000, priceUSD: 35.00, name: 'Gold Pack' },
    { id: 'cb5', coins: 100000, priceUSD: 65.00, name: 'Platinum Pack' },
    { id: 'cb6', coins: 500000, priceUSD: 300.00, name: 'Diamond Pack' },
    { id: 'cb7', coins: 1000000, priceUSD: 550.00, name: 'Mythic Pack' },
    { id: 'cb8', coins: null, priceUSD: null, name: 'Custom Amount', isCustom: true }
];

const EXCHANGE_RATES = { USD: 1, EUR: 0.92, GBP: 0.79, CAD: 1.36, AUD: 1.52 };
const CURRENCY_SYMBOLS = { USD: '$', EUR: '€', GBP: '£', CAD: 'C$', AUD: 'A$' };

let cart = JSON.parse(localStorage.getItem('crix_cart') || '[]');
let activeSale = null;
let activeDiscount = 0;
let couponApplied = false;
let selectedPayment = null;
let cardSplit = [{ cardId: '', amount: 0 }];

function getCurrency() { return localStorage.getItem('crix_currency') || 'USD'; }
function setCurrency(currency) { localStorage.setItem('crix_currency', currency); renderPackages(); updateCartDisplay(); }

function convertPrice(usdPrice) {
    if (!usdPrice) return null;
    const rate = EXCHANGE_RATES[getCurrency()];
    return { amount: (usdPrice * rate).toFixed(2), symbol: CURRENCY_SYMBOLS[getCurrency()] };
}

async function loadActiveSale() {
    try {
        const response = await fetch('data/sales.json?t=' + Date.now());
        const sales = await response.json();
        const now = new Date();
        for (const sale of sales) {
            if (now >= new Date(sale.start) && now <= new Date(sale.end) && sale.active !== false) {
                activeSale = sale;
                activeDiscount = sale.discount;
                document.getElementById('saleBanner').style.display = 'block';
                document.getElementById('saleBanner').innerHTML = `🎉 ${sale.name} SALE! ${sale.discount}% OFF! 🎉`;
                break;
            }
        }
    } catch(e) { console.log('No active sale'); }
}

function renderPackages() {
    const grid = document.getElementById('packagesGrid');
    if (!grid) return;
    grid.innerHTML = '';
    for (const pkg of PACKAGES) {
        const card = document.createElement('div');
        card.className = 'package-card';
        if (pkg.isCustom) {
            card.innerHTML = `
                <img src="images/${pkg.id}.png" class="package-img" onerror="this.src='https://placehold.co/200x150/2A43E0/white?text=CUSTOM'">
                <div class="package-info">
                    <h3>${pkg.name}</h3>
                    <div class="coin-amount">Custom Coins</div>
                    <button class="buy-btn custom-btn" onclick="openCustomAmount()">Custom →</button>
                </div>
            `;
        } else {
            let priceUSD = pkg.priceUSD;
            if (activeDiscount > 0) priceUSD *= (1 - activeDiscount / 100);
            if (couponApplied) priceUSD *= 0.8;
            const display = convertPrice(priceUSD);
            card.innerHTML = `
                <img src="images/${pkg.id}.png" class="package-img" onerror="this.src='https://placehold.co/200x150/2A43E0/white?text=${pkg.name}'">
                <div class="package-info">
                    <h3>${pkg.name}</h3>
                    <div class="coin-amount">${pkg.coins.toLocaleString()} Coins</div>
                    <div class="price">${display.symbol}${display.amount}</div>
                    <div class="bonus">+${Math.floor(pkg.coins * 0.1).toLocaleString()} Free!</div>
                    <button class="buy-btn" onclick="addToCart('${pkg.id}', ${pkg.coins}, ${priceUSD})">Add to Cart →</button>
                </div>
            `;
        }
        grid.appendChild(card);
    }
}

function addToCart(id, coins, priceUSD) {
    const existing = cart.find(i => i.id === id);
    if (existing) existing.quantity++;
    else cart.push({ id, coins, priceUSD, quantity: 1 });
    localStorage.setItem('crix_cart', JSON.stringify(cart));
    updateCartCount();
    showNotification(`Added ${coins.toLocaleString()} Coins!`);
}

function openCustomAmount() {
    const coins = parseInt(prompt('Enter amount (min 1,000):', '1000'));
    if (isNaN(coins) || coins < 1000) { alert('Minimum 1,000 Coins'); return; }
    let priceUSD = coins / 1000;
    if (activeDiscount > 0) priceUSD *= (1 - activeDiscount / 100);
    if (couponApplied) priceUSD *= 0.8;
    addToCart('custom', coins, priceUSD);
}

function updateCartCount() {
    document.getElementById('cartCount').innerText = cart.reduce((s,i) => s + i.quantity, 0);
}

function openCart() { updateCartDisplay(); document.getElementById('cartModal').style.display = 'flex'; }
function closeCart() { document.getElementById('cartModal').style.display = 'none'; }

function updateCartDisplay() {
    const container = document.getElementById('cartItems');
    const totalDiv = document.getElementById('cartTotal');
    if (!container) return;
    if (cart.length === 0) {
        container.innerHTML = '<p style="text-align:center; padding:2rem;">Your cart is empty.</p>';
        totalDiv.innerHTML = 'Total: $0.00';
        return;
    }
    let totalUSD = 0;
    let html = '';
    for (const item of cart) {
        const itemTotal = item.priceUSD * item.quantity;
        totalUSD += itemTotal;
        html += `<div class="cart-item">
            <div><strong>${item.coins.toLocaleString()} Coins</strong> x${item.quantity}</div>
            <div>${convertPrice(itemTotal).symbol}${convertPrice(itemTotal).amount} <button class="remove-item" onclick="removeFromCart('${item.id}')">×</button></div>
        </div>`;
    }
    const displayTotal = convertPrice(totalUSD);
    container.innerHTML = html;
    totalDiv.innerHTML = `Total: ${displayTotal.symbol}${displayTotal.amount}`;
    if (activeDiscount > 0) totalDiv.innerHTML += `<div style="font-size:0.8rem;color:#22c55e;">🎉 ${activeDiscount}% Sale Applied!</div>`;
    if (couponApplied) totalDiv.innerHTML += `<div style="font-size:0.8rem;color:#22c55e;">💎 Creator Code 20% Applied!</div>`;
}

function removeFromCart(id) {
    cart = cart.filter(i => i.id !== id);
    localStorage.setItem('crix_cart', JSON.stringify(cart));
    updateCartCount();
    updateCartDisplay();
}

function applyCoupon() {
    const code = document.getElementById('couponCode').value.toLowerCase();
    if (code === 'cgvr') {
        couponApplied = true;
        showNotification('20% OFF Applied!');
        renderPackages();
        updateCartDisplay();
    } else { showNotification('Invalid code!', 'error'); }
}

function selectPayment(method) {
    selectedPayment = method;
    document.getElementById('paypalRadio').checked = (method === 'paypal');
    document.getElementById('cardRadio').checked = (method === 'card');
    if (method === 'card') {
        loadCardsForSplit();
    } else {
        document.getElementById('cardSplitContainer').style.display = 'none';
    }
}

async function loadCardsForSplit() {
    try {
        const response = await fetch('data/cards.json');
        const cards = await response.json();
        let html = '<div class="card-split-container"><h4>Split Payment Across Cards</h4>';
        cardSplit.forEach((split, idx) => {
            html += `<div class="card-split-item">
                <select onchange="updateCardSplit(${idx}, 'card', this.value)">
                    <option value="">Select Card</option>
                    ${cards.map(c => `<option value="${c.id}">${c.name} (****${c.last4}) - $${c.balance}</option>`).join('')}
                </select>
                <input type="number" placeholder="Amount $" onchange="updateCardSplit(${idx}, 'amount', this.value)">
                ${idx > 0 ? `<button onclick="removeCardSplit(${idx})">×</button>` : ''}
            </div>`;
        });
        html += `<button class="add-card-btn" onclick="addCardSplit()">+ Add Another Card</button>
                <div class="split-total" id="splitTotal"></div></div>`;
        document.getElementById('cardSplitContainer').innerHTML = html;
        document.getElementById('cardSplitContainer').style.display = 'block';
    } catch(e) { console.error('Failed to load cards'); }
}

function updateCardSplit(idx, field, value) {
    if (field === 'card') cardSplit[idx].cardId = value;
    if (field === 'amount') cardSplit[idx].amount = parseFloat(value) || 0;
    updateSplitTotal();
}

function addCardSplit() { cardSplit.push({ cardId: '', amount: 0 }); loadCardsForSplit(); }
function removeCardSplit(idx) { cardSplit.splice(idx, 1); loadCardsForSplit(); }

function updateSplitTotal() {
    const total = cardSplit.reduce((s, i) => s + i.amount, 0);
    document.getElementById('splitTotal').innerHTML = `Total to charge: $${total.toFixed(2)}`;
}

function showNotification(msg, type = 'success') {
    const notif = document.createElement('div');
    notif.style.cssText = `position:fixed;bottom:20px;right:20px;background:${type === 'error' ? '#FF2A1F' : '#22c55e'};color:white;padding:12px 20px;border-radius:8px;z-index:9999;animation:fadeInOut 3s forwards;`;
    notif.innerText = msg;
    document.body.appendChild(notif);
    setTimeout(() => notif.remove(), 3000);
}

function checkout() {
    if (cart.length === 0) { alert('Cart is empty!'); return; }
    if (!selectedPayment) { alert('Select a payment method!'); return; }
    let totalUSD = cart.reduce((s, i) => s + (i.priceUSD * i.quantity), 0);
    const totalCoins = cart.reduce((s, i) => s + (i.coins * i.quantity), 0);
    
    const paymentData = { totalUSD, totalCoins, items: cart, method: selectedPayment };
    if (selectedPayment === 'card') {
        const totalSplit = cardSplit.reduce((s, i) => s + i.amount, 0);
        if (Math.abs(totalSplit - totalUSD) > 0.01) {
            alert(`Split total ($${totalSplit.toFixed(2)}) doesn't match order total ($${totalUSD.toFixed(2)})`);
            return;
        }
        paymentData.cardSplit = cardSplit.filter(c => c.cardId && c.amount > 0);
    }
    localStorage.setItem('crix_pending_payment', JSON.stringify(paymentData));
    alert(`Order placed! Total: $${totalUSD.toFixed(2)} for ${totalCoins.toLocaleString()} Coins\nPayment will be processed.`);
    cart = [];
    localStorage.setItem('crix_cart', JSON.stringify(cart));
    updateCartCount();
    closeCart();
}

document.getElementById('currencySelect')?.addEventListener('change', (e) => setCurrency(e.target.value));
document.getElementById('currencySelect').value = getCurrency();
loadActiveSale();
renderPackages();
updateCartCount();

const style = document.createElement('style');
style.textContent = `@keyframes fadeInOut {0%{opacity:0;transform:translateY(20px);}15%{opacity:1;transform:translateY(0);}85%{opacity:1;}100%{opacity:0;transform:translateY(-20px);}}`;
document.head.appendChild(style);

window.openCart = openCart;
window.closeCart = closeCart;
window.addToCart = addToCart;
window.removeFromCart = removeFromCart;
window.applyCoupon = applyCoupon;
window.selectPayment = selectPayment;
window.openCustomAmount = openCustomAmount;
window.updateCardSplit = updateCardSplit;
window.addCardSplit = addCardSplit;
window.removeCardSplit = removeCardSplit;
window.checkout = checkout;