import { api, getCurrentUser, clearAuth } from '../api/client';
import { navigate } from '../router';
import { getApp, showToast } from '../utils';

export function renderSettings(): void {
  const app = getApp();
  const user = getCurrentUser();

  app.innerHTML = `
    <header class="app-header">
      <div class="logo">
        <span class="gradient-text">📸 PhotoViewer</span>
      </div>
      <nav class="header-nav">
        <button class="nav-link" onclick="location.hash='#/gallery'">Gallery</button>
        <button class="nav-link" onclick="location.hash='#/downloads'">Downloads</button>
        <button class="nav-link" onclick="location.hash='#/duplicates'">Duplicates</button>
        ${user?.role === 'Admin' ? '<button class="nav-link" onclick="location.hash=\'#/users\'">Users</button>' : ''}
      </nav>
      <div class="header-actions">
        <div class="user-badge">👤 ${user?.username || 'User'}</div>
        <button class="btn btn-sm" id="logout-btn">Logout</button>
      </div>
    </header>
    <div class="content-area">
      <div class="settings-page">
        <div class="settings-card card">
          <div class="card-header">
            <h3>🔒 Change Password</h3>
          </div>
          <div class="card-body">
            <div id="pw-error" class="login-error" style="display:none"></div>
            <div id="pw-success" class="pw-success" style="display:none"></div>
            <form id="change-pw-form" autocomplete="off">
              <div class="input-group" style="margin-bottom:16px">
                <label>Current Password</label>
                <input type="password" class="input" id="current-pw" required autocomplete="current-password" />
              </div>
              <div class="input-group" style="margin-bottom:16px">
                <label>New Password <span style="color:var(--text-muted);font-weight:400">(min 8 characters)</span></label>
                <input type="password" class="input" id="new-pw" required minlength="8" autocomplete="new-password" />
              </div>
              <div class="input-group" style="margin-bottom:24px">
                <label>Confirm New Password</label>
                <input type="password" class="input" id="confirm-pw" required minlength="8" autocomplete="new-password" />
              </div>
              <button type="submit" class="btn btn-primary" id="change-pw-btn">Change Password</button>
            </form>
          </div>
        </div>
      </div>
    </div>
  `;

  document.getElementById('logout-btn')?.addEventListener('click', () => {
    clearAuth();
    navigate('/login');
  });

  document.getElementById('change-pw-form')?.addEventListener('submit', async (e) => {
    e.preventDefault();

    const currentPw = (document.getElementById('current-pw') as HTMLInputElement).value;
    const newPw = (document.getElementById('new-pw') as HTMLInputElement).value;
    const confirmPw = (document.getElementById('confirm-pw') as HTMLInputElement).value;
    const errorEl = document.getElementById('pw-error')!;
    const successEl = document.getElementById('pw-success')!;
    const btn = document.getElementById('change-pw-btn') as HTMLButtonElement;

    errorEl.style.display = 'none';
    successEl.style.display = 'none';

    if (newPw.length < 8) {
      errorEl.textContent = 'New password must be at least 8 characters';
      errorEl.style.display = 'block';
      return;
    }

    if (newPw !== confirmPw) {
      errorEl.textContent = 'New passwords do not match';
      errorEl.style.display = 'block';
      return;
    }

    btn.disabled = true;
    btn.textContent = 'Changing...';

    try {
      await api.changePassword(currentPw, newPw);
      successEl.textContent = '✅ Password changed successfully!';
      successEl.style.display = 'block';
      (document.getElementById('current-pw') as HTMLInputElement).value = '';
      (document.getElementById('new-pw') as HTMLInputElement).value = '';
      (document.getElementById('confirm-pw') as HTMLInputElement).value = '';
    } catch (err: any) {
      errorEl.textContent = err.message || 'Failed to change password';
      errorEl.style.display = 'block';
    } finally {
      btn.disabled = false;
      btn.textContent = 'Change Password';
    }
  });
}
