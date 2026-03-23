import { api, setAuth } from '../api/client';
import { navigate } from '../router';
import { getApp, showToast } from '../utils';

export function renderLogin(): void {
  const app = getApp();
  app.innerHTML = `
    <div class="login-page">
      <div class="login-bg"></div>
      <div class="login-card">
        <h1><span class="gradient-text">📸 PhotoViewer</span></h1>
        <p class="subtitle">Sign in to browse your photo library</p>
        <div id="login-error" class="login-error" style="display:none"></div>
        <form id="login-form">
          <div class="input-group">
            <label for="username">Username</label>
            <input type="text" id="username" class="input" placeholder="Enter username" autocomplete="username" autofocus />
          </div>
          <div class="input-group">
            <label for="password">Password</label>
            <input type="password" id="password" class="input" placeholder="Enter password" autocomplete="current-password" />
          </div>
          <button type="submit" class="btn btn-primary" id="login-btn">Sign In</button>
        </form>
      </div>
    </div>
  `;

  const form = document.getElementById('login-form')!;
  form.addEventListener('submit', async (e) => {
    e.preventDefault();
    const username = (document.getElementById('username') as HTMLInputElement).value;
    const password = (document.getElementById('password') as HTMLInputElement).value;
    const errorEl = document.getElementById('login-error')!;
    const btn = document.getElementById('login-btn') as HTMLButtonElement;

    if (!username || !password) {
      errorEl.textContent = 'Please enter both username and password';
      errorEl.style.display = 'block';
      return;
    }

    btn.disabled = true;
    btn.textContent = 'Signing in...';
    errorEl.style.display = 'none';

    try {
      const result = await api.login(username, password);
      setAuth(result.token, result.username, result.role);
      showToast(`Welcome back, ${result.username}!`);
      navigate('/gallery');
    } catch (err: any) {
      errorEl.textContent = err.message || 'Login failed';
      errorEl.style.display = 'block';
      btn.disabled = false;
      btn.textContent = 'Sign In';
    }
  });
}
