// CRIX SMP - Shop & Cart System

// Coin packages (cb1.png to cb8.png)
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

// Exchange rates (USD base)
const EXCHANGE_RATES = {
    USD: 1,
    EUR: 0.92,
    GBP: 0.79,
    CAD: 1.36,
    AUD: 1.52,
    JPY: 151.45
};

// Currency symbols
const CURRENCY_SYMBOLS = {
    USD: '$',
    EUR: '€',
    GBP: '£',
    CAD: 'C$',
    AUD: 'A$',
    JPY: '¥'
};

// Cart state
let cart = JSON.parse(localStorage.getItem('crix_cart') || '[]');
let activeSale = null;
let activeDiscount = 0;
let couponApplied = false;

// Load active sale from server/localStorage
async function loadActiveSale() {
    try {
        const response = await fetch('sales.json?t=' + Date.now());
        const sales = await response.json();
        const now = new Date();
        
        for (const sale of sales) {
            const start = new Date(sale.start);
            const end = new Date(sale.end);
            if (now >= start && now <= end && sale.active !== false) {
                activeSale = sale;
                activeDiscount = sale.discount;
                const banner = document.getElementById('saleBanner');
                if (banner) {
                    banner.style.display = 'block';
                    banner.innerHTML = `🎉 ${sale.name} SALE! ${sale.discount}% OFF on all purchases! 🎉`;
                }
                break;
            }
        }
    } catch (e) {
        console.log('No active sale');
    }
}

// Get current currency
function getCurrency() {
    return localStorage.getItem('crix_currency') || 'USD';
}

// Set currency
function setCurrency(currency) {
    localStorage.setItem('crix_currency', currency);
    renderPackages();
    updateCartDisplay();
}

// Convert price based on currency
function convertPrice(usdPrice) {
    if (!usdPrice) return null;
    const currency = getCurrency();
    const rate = EXCHANGE_RATES[currency];
    const converted = usdPrice * rate;
    const symbol = CURRENCY_SYMBOLS[currency];
    return { amount: converted.toFixed(2), symbol };
}

// Apply sale discount
function applySaleDiscount(priceUSD) {
    if (activeSale && activeDiscount > 0) {
        return priceUSD * (1 - activeDiscount / 100);
    }
    return priceUSD;
}

// Apply creator code discount (20% off)
function applyCreatorDiscount(priceUSD, hasCode) {
    if (hasCode) {
        return priceUSD * 0.8;
    }
    return priceUSD;
}

// Render coin packages on page
function renderPackages() {
    const grid = document.getElementById('packagesGrid');
    if (!grid) return;
    
    grid.innerHTML = '';
    
    for (const pkg of PACKAGES) {
        const card = document.createElement('div');
        card.className = 'package-card';
        
        let priceHTML = '';
        let buttonHTML = '';
        
        if (pkg.isCustom) {
            priceHTML = `<div class="price">Custom amount</div>`;
            buttonHTML = `<button class="buy-btn custom-btn" onclick="openCustomAmount()">Custom →</button>`;
        } else {
            const originalPrice = convertPrice(pkg.priceUSD);
            const salePriceUSD = applySaleDiscount(pkg.priceUSD);
            const finalPriceUSD = applyCreatorDiscount(salePriceUSD, couponApplied);
            const displayPrice = convertPrice(finalPriceUSD);
            
            let priceDisplay = `<div class="price">${displayPrice.symbol}${displayPrice.amount} ${getCurrency()}</div>`;
            if (activeDiscount > 0) {
                priceDisplay = `<div class="price"><span style="text-decoration:line-through;color:#888;">${originalPrice.symbol}${originalPrice.amount}</span> → ${displayPrice.symbol}${displayPrice.amount}</div>`;
            }
            
            priceHTML = priceDisplay;
            buttonHTML = `<button class="buy-btn" onclick="addToCart('${pkg.id}', ${pkg.coins}, ${finalPriceUSD})">Add to Cart →</button>`;
        }
        
        card.innerHTML = `
            <img src="${pkg.id}.png" alt="${pkg.name}" class="package-img" onerror="this.src='https://placehold.co/200x150/2A43E0/white?text=${pkg.name}'">
            <div class="package-info">
                <h3>${pkg.name}</h3>
                <div class="coin-amount">${pkg.coins ? pkg.coins.toLocaleString() : 'Custom'} Coins</div>
                ${priceHTML}
                ${pkg.coins ? `<div class="bonus">+${Math.floor(pkg.coins * 0.1).toLocaleString()} Free!</div>` : '<div class="bonus">10% Bonus!</div>'}
                ${buttonHTML}
            </div>
        `;
        grid.appendChild(card);
    }
}

// Add item to cart
function addToCart(packageId, coins, priceUSD) {
    const existing = cart.find(item => item.id === packageId);
    if (existing) {
        existing.quantity++;
    } else {
        cart.push({ id: packageId, coins, priceUSD, quantity: 1 });
    }
    saveCart();
    updateCartCount();
    showNotification(`Added ${coins.toLocaleString()} Coins to cart!`);
}

// Save cart to localStorage
function saveCart() {
    localStorage.setItem('crix_cart', JSON.stringify(cart));
    updateCartCount();
}

// Update cart count badge
function updateCartCount() {
    const count = cart.reduce((sum, item) => sum + item.quantity, 0);
    const cartCount = document.getElementById('cartCount');
    if (cartCount) cartCount.innerText = count;
}

// Open cart modal
function openCart() {
    updateCartDisplay();
    document.getElementById('cartModal').style.display = 'flex';
}

// Close cart modal
function closeCart() {
    document.getElementById('cartModal').style.display = 'none';
}

// Update cart display
function updateCartDisplay() {
    const cartItemsDiv = document.getElementById('cartItems');
    const cartTotalDiv = document.getElementById('cartTotal');
    
    if (!cartItemsDiv) return;
    
    if (cart.length === 0) {
        cartItemsDiv.innerHTML = '<p style="text-align:center; padding:2rem;">Your cart is empty.</p>';
        cartTotalDiv.innerHTML = 'Total: $0.00';
        return;
    }
    
    let html = '';
    let totalUSD = 0;
    const currency = getCurrency();
    const symbol = CURRENCY_SYMBOLS[currency];
    
    for (const item of cart) {
        const itemTotalUSD = item.priceUSD * item.quantity;
        totalUSD += itemTotalUSD;
        
        let salePriceUSD = applySaleDiscount(itemTotalUSD);
        let finalPriceUSD = applyCreatorDiscount(salePriceUSD, couponApplied);
        const displayTotal = convertPrice(finalPriceUSD);
        
        html += `
            <div class="cart-item">
                <div>
                    <strong>${item.coins.toLocaleString()} Coins</strong>
                    <div>x${item.quantity}</div>
                </div>
                <div>
                    ${symbol}${(displayTotal.amount)}
                    <button onclick="removeFromCart('${item.id}')" style="background:#FF2A1F; margin-left:10px; padding:2px 8px;">×</button>
                </div>
            </div>
        `;
    }
    
    let finalTotalUSD = applySaleDiscount(totalUSD);
    finalTotalUSD = applyCreatorDiscount(finalTotalUSD, couponApplied);
    const finalDisplay = convertPrice(finalTotalUSD);
    
    cartItemsDiv.innerHTML = html;
    cartTotalDiv.innerHTML = `Subtotal: ${symbol}${finalDisplay.amount} ${currency}`;
    if (activeDiscount > 0) {
        cartTotalDiv.innerHTML += `<div style="font-size:0.8rem; color:#22c55e;">🎉 ${activeDiscount}% Sale Applied!</div>`;
    }
    if (couponApplied) {
        cartTotalDiv.innerHTML += `<div style="font-size:0.8rem; color:#22c55e;">💎 Creator Code 20% Applied!</div>`;
    }
}

// Remove from cart
function removeFromCart(packageId) {
    cart = cart.filter(item => item.id !== packageId);
    saveCart();
    updateCartDisplay();
}

// Apply coupon code
function applyCoupon() {
    const code = document.getElementById('couponCode').value.toLowerCase();
    if (code === 'cgvr') {
        couponApplied = true;
        showNotification('Creator code applied! 20% OFF!');
        updateCartDisplay();
        renderPackages();
    } else {
        showNotification('Invalid code! Try "cgvr"', 'error');
    }
}

// Open custom amount modal
function openCustomAmount() {
    const amount = prompt('Enter the amount of CRIX Coins you want to purchase (minimum 1,000):', '1000');
    const coins = parseInt(amount);
    if (isNaN(coins) || coins < 1000) {
        alert('Minimum custom amount is 1,000 Coins');
        return;
    }
    let priceUSD = coins / 1000;
    priceUSD = applySaleDiscount(priceUSD);
    priceUSD = applyCreatorDiscount(priceUSD, couponApplied);
    addToCart('custom', coins, priceUSD);
}

// Checkout
function checkout() {
    if (cart.length === 0) {
        alert('Your cart is empty!');
        return;
    }
    
    let totalUSD = cart.reduce((sum, item) => sum + (item.priceUSD * item.quantity), 0);
    totalUSD = applySaleDiscount(totalUSD);
    totalUSD = applyCreatorDiscount(totalUSD, couponApplied);
    const currency = getCurrency();
    const converted = convertPrice(totalUSD);
    
    if (confirm(`Proceed to purchase ${cart.reduce((s,i)=>s+i.quantity,0)} items for ${converted.symbol}${converted.amount} ${currency}?`)) {
        // Redirect to payment (PayPal/Stripe)
        alert('Redirecting to payment gateway...');
        // window.location.href = '/checkout';
        cart = [];
        saveCart();
        closeCart();
        updateCartCount();
    }
}

// Show notification
function showNotification(msg, type = 'success') {
    const notif = document.createElement('div');
    notif.style.cssText = `position:fixed;bottom:20px;right:20px;background:${type === 'error' ? '#FF2A1F' : '#22c55e'};color:white;padding:12px 20px;border-radius:8px;z-index:3000;animation:fadeInOut 3s forwards;`;
    notif.innerText = msg;
    document.body.appendChild(notif);
    setTimeout(() => notif.remove(), 3000);
}

// Currency selector change
document.addEventListener('DOMContentLoaded', () => {
    const currencySelect = document.getElementById('currencySelect');
    if (currencySelect) {
        currencySelect.value = getCurrency();
        currencySelect.onchange = (e) => setCurrency(e.target.value);
    }
    
    loadActiveSale();
    renderPackages();
    updateCartCount();
});

// Add animation style
const style = document.createElement('style');
style.textContent = `@keyframes fadeInOut {0%{opacity:0;transform:translateY(20px);}15%{opacity:1;transform:translateY(0);}85%{opacity:1;}100%{opacity:0;transform:translateY(-20px);}}`;
document.head.appendChild(style);