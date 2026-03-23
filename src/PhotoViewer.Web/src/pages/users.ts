import { api, getCurrentUser, clearAuth } from '../api/client';
import { navigate } from '../router';
import { getApp, showToast, formatDate } from '../utils';

export function renderUsers(): void {
  const app = getApp();
  const user = getCurrentUser();

  if (user?.role !== 'Admin') {
    navigate('/gallery');
    return;
  }

  app.innerHTML = `
    <header class="app-header">
      <div class="logo">
        <span class="gradient-text">📸 PhotoViewer</span>
      </div>
      <nav class="header-nav">
        <button class="nav-link" onclick="location.hash='#/gallery'">Gallery</button>
        <button class="nav-link" onclick="location.hash='#/downloads'">Downloads</button>
        <button class="nav-link" onclick="location.hash='#/duplicates'">Duplicates</button>
        <button class="nav-link active" onclick="location.hash='#/users'">Users</button>
      </nav>
      <div class="header-actions">
        <div class="user-badge">👤 ${user?.username || 'User'}</div>
        <button class="btn btn-sm" id="logout-btn">Logout</button>
      </div>
    </header>
    <div class="content-area">
      <div class="page-header">
        <h2>User Management</h2>
      </div>
      <div class="add-user-form" id="add-user-form">
        <div class="input-group">
          <label for="new-user">Username</label>
          <input type="text" id="new-user" class="input" placeholder="Enter username" />
        </div>
        <div class="input-group">
          <label for="new-pass">Password</label>
          <input type="password" id="new-pass" class="input" placeholder="Enter password" />
        </div>
        <div class="input-group">
          <label for="new-role">Role</label>
          <select id="new-role" class="input">
            <option value="User">User</option>
            <option value="Admin">Admin</option>
          </select>
        </div>
        <button class="btn btn-primary" id="add-user-btn">+ Add User</button>
      </div>
      <div id="users-grid" class="users-grid">
        <div class="loading-spinner"></div>
      </div>
    </div>
  `;

  document.getElementById('logout-btn')?.addEventListener('click', () => {
    clearAuth();
    navigate('/login');
  });

  document.getElementById('add-user-btn')?.addEventListener('click', addUser);
  loadUsers();
}

async function loadUsers(): Promise<void> {
  const grid = document.getElementById('users-grid')!;

  try {
    const users = await api.getUsers();
    grid.innerHTML = '';

    users.forEach(user => {
      const card = document.createElement('div');
      card.className = 'user-card';
      card.innerHTML = `
        <div class="user-info">
          <div class="user-avatar">${user.username.charAt(0).toUpperCase()}</div>
          <div class="user-details">
            <div class="username">${user.username}</div>
            <div class="role">${user.role} · Created ${formatDate(user.createdAt)}</div>
          </div>
        </div>
        ${user.id !== 1 ? `<button class="btn btn-danger btn-sm" data-delete="${user.id}">Delete</button>` : '<span class="status-badge ready">Default</span>'}
      `;
      grid.appendChild(card);

      card.querySelector(`[data-delete="${user.id}"]`)?.addEventListener('click', async () => {
        if (!confirm(`Delete user "${user.username}"?`)) return;
        try {
          await api.deleteUser(user.id);
          showToast('User deleted');
          loadUsers();
        } catch (err: any) {
          showToast(err.message || 'Failed to delete', 'error');
        }
      });
    });
  } catch {
    grid.innerHTML = '<p style="color:var(--text-muted)">Failed to load users</p>';
  }
}

async function addUser(): Promise<void> {
  const username = (document.getElementById('new-user') as HTMLInputElement).value.trim();
  const password = (document.getElementById('new-pass') as HTMLInputElement).value;
  const role = (document.getElementById('new-role') as HTMLSelectElement).value;

  if (!username || !password) {
    showToast('Please enter username and password', 'error');
    return;
  }

  try {
    await api.createUser(username, password, role);
    showToast(`User "${username}" created!`);
    (document.getElementById('new-user') as HTMLInputElement).value = '';
    (document.getElementById('new-pass') as HTMLInputElement).value = '';
    loadUsers();
  } catch (err: any) {
    showToast(err.message || 'Failed to create user', 'error');
  }
}
