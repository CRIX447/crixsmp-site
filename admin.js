// CRIX SMP - Admin Panel
// Whitelisted IPs (replace with your actual IPs)
const WHITELISTED_IPS = ['127.0.0.1', 'localhost', 'YOUR_IP_HERE'];

// Get user IP (simplified - in production, check server-side)
async function getUserIP() {
    try {
        const response = await fetch('https://api.ipify.org?format=json');
        const data = await response.json();
        return data.ip;
    } catch {
        return '127.0.0.1';
    }
}

// Check access
async function checkAccess() {
    const ip = await getUserIP();
    const isWhitelisted = WHITELISTED_IPS.includes(ip) || WHITELISTED_IPS.includes('YOUR_IP_HERE');
    
    if (!isWhitelisted) {
        document.getElementById('accessDenied').style.display = 'block';
        document.getElementById('adminPanel').style.display = 'none';
    } else {
        document.getElementById('accessDenied').style.display = 'none';
        document.getElementById('adminPanel').style.display = 'block';
        loadSalesList();
        loadCurrentSale();
        loadIPList();
    }
}

// Load sales list into dropdown
async function loadSalesList() {
    try {
        const response = await fetch('sales.json?t=' + Date.now());
        const sales = await response.json();
        const select = document.getElementById('saleSelect');
        select.innerHTML = '<option value="">-- Select Sale to Activate --</option>';
        
        for (const sale of sales) {
            const option = document.createElement('option');
            option.value = sale.name;
            option.textContent = `${sale.name} (${sale.discount}% off) - ${sale.start} to ${sale.end}`;
            select.appendChild(option);
        }
    } catch (e) {
        console.error('Failed to load sales:', e);
    }
}

// Load current active sale info
async function loadCurrentSale() {
    try {
        const response = await fetch('sales.json?t=' + Date.now());
        const sales = await response.json();
        const now = new Date();
        let active = null;
        
        for (const sale of sales) {
            const start = new Date(sale.start);
            const end = new Date(sale.end);
            if (now >= start && now <= end && sale.active !== false) {
                active = sale;
                break;
            }
        }
        
        const infoDiv = document.getElementById('currentSaleInfo');
        if (active) {
            infoDiv.innerHTML = `<strong>Current Active Sale:</strong> ${active.name} - ${active.discount}% OFF (until ${active.end})`;
        } else {
            infoDiv.innerHTML = `<strong>No active sale at this time.</strong>`;
        }
    } catch (e) {
        console.error('Failed to load current sale:', e);
    }
}

// Activate custom sale
function activateCustomSale() {
    const discount = document.getElementById('customDiscount').value;
    const name = document.getElementById('customSaleName').value;
    const startDate = document.getElementById('customStartDate').value;
    const endDate = document.getElementById('customEndDate').value;
    
    if (!discount || !name || !startDate || !endDate) {
        alert('Please fill in all fields');
        return;
    }
    
    // Create custom sale object
    const customSale = {
        name: name.toUpperCase(),
        start: startDate,
        end: endDate,
        discount: parseInt(discount),
        active: true,
        isCustom: true
    };
    
    // Store custom sale in localStorage
    localStorage.setItem('crix_custom_sale', JSON.stringify(customSale));
    
    // Also update the sales.json via fetch (in production, this would be a server call)
    alert(`Custom sale "${name}" activated with ${discount}% discount!`);
    location.reload();
}

// Deactivate current sale
function deactivateSale() {
    localStorage.removeItem('crix_custom_sale');
    alert('Sale deactivated!');
    location.reload();
}

// Load whitelisted IPs
function loadIPList() {
    let ips = JSON.parse(localStorage.getItem('crix_whitelisted_ips') || '[]');
    if (ips.length === 0) {
        ips = ['127.0.0.1', 'localhost'];
        localStorage.setItem('crix_whitelisted_ips', JSON.stringify(ips));
    }
    
    const ipListDiv = document.getElementById('ipList');
    ipListDiv.innerHTML = ips.map(ip => `<div style="display:flex; justify-content:space-between; padding:0.25rem;"><span>${ip}</span><button onclick="removeIP('${ip}')" style="background:#FF2A1F; padding:2px 8px;">Remove</button></div>`).join('');
}

// Add IP to whitelist
function addIP() {
    const newIp = document.getElementById('newIp').value.trim();
    if (!newIp) return;
    
    let ips = JSON.parse(localStorage.getItem('crix_whitelisted_ips') || '[]');
    if (!ips.includes(newIp)) {
        ips.push(newIp);
        localStorage.setItem('crix_whitelisted_ips', JSON.stringify(ips));
        loadIPList();
        alert(`Added ${newIp} to whitelist`);
    } else {
        alert('IP already in whitelist');
    }
}

// Remove IP from whitelist
function removeIP(ip) {
    let ips = JSON.parse(localStorage.getItem('crix_whitelisted_ips') || '[]');
    ips = ips.filter(i => i !== ip);
    localStorage.setItem('crix_whitelisted_ips', JSON.stringify(ips));
    loadIPList();
}

// Edit creator code
function editCreatorCode() {
    const newCode = prompt('Enter new creator code (leave blank for default "cgvr"):', 'cgvr');
    if (newCode !== null) {
        localStorage.setItem('crix_creator_code', newCode.toLowerCase());
        document.getElementById('creatorCodeDisplay').innerText = newCode.toLowerCase();
        alert(`Creator code changed to "${newCode.toLowerCase()}" (20% off)`);
    }
}

// Logout - clear session
function logout() {
    localStorage.removeItem('crix_admin_auth');
    alert('Logged out. Refresh to re-authenticate.');
}

// Event listeners
document.addEventListener('DOMContentLoaded', () => {
    checkAccess();
    
    // Display creator code
    const savedCode = localStorage.getItem('crix_creator_code') || 'cgvr';
    const codeDisplay = document.getElementById('creatorCodeDisplay');
    if (codeDisplay) codeDisplay.innerText = savedCode;
    
    // Buttons
    const activateBtn = document.getElementById('activateCustomSale');
    if (activateBtn) activateBtn.onclick = activateCustomSale;
    
    const deactivateBtn = document.getElementById('deactivateSale');
    if (deactivateBtn) deactivateBtn.onclick = deactivateSale;
    
    const addIpBtn = document.getElementById('addIpBtn');
    if (addIpBtn) addIpBtn.onclick = addIP;
    
    const editCodeBtn = document.getElementById('editCreatorCodeBtn');
    if (editCodeBtn) editCodeBtn.onclick = editCreatorCode;
    
    const logoutBtn = document.getElementById('logoutBtn');
    if (logoutBtn) logoutBtn.onclick = logout;
});

// Make removeIP globally available
window.removeIP = removeIP;