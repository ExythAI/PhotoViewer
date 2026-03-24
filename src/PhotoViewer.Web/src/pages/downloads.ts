import { api, getCurrentUser, clearAuth } from '../api/client';
import { navigate, setCleanup } from '../router';
import { getApp, formatFileSize, showToast, formatDate } from '../utils';

interface DownloadItem {
  id: number;
  status: string;
  zipFileName: string | null;
  zipFileSize: number | null;
  createdAt: string;
  completedAt: string | null;
}

let pollIntervals: ReturnType<typeof setInterval>[] = [];

export function renderDownloads(): void {
  const app = getApp();
  const user = getCurrentUser();

  app.innerHTML = `
    ${renderHeaderNav(user)}
    <div class="content-area">
      <div class="page-header">
        <h2>Downloads</h2>
        <button class="btn btn-sm" id="refresh-downloads">🔄 Refresh</button>
      </div>
      <div id="downloads-list" class="downloads-list">
        <div class="loading-spinner"></div>
      </div>
      <div id="downloads-empty" class="gallery-empty" style="display:none">
        <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.5">
          <path d="M21 15v4a2 2 0 01-2 2H5a2 2 0 01-2-2v-4"/>
          <polyline points="7 10 12 15 17 10"/>
          <line x1="12" y1="15" x2="12" y2="3"/>
        </svg>
        <h3>No downloads yet</h3>
        <p>Select files from the gallery and click "Download as ZIP" to create a download.</p>
      </div>
    </div>
  `;

  document.getElementById('refresh-downloads')?.addEventListener('click', loadDownloads);
  document.getElementById('logout-btn')?.addEventListener('click', () => {
    clearAuth();
    navigate('/login');
  });

  loadDownloads();

  setCleanup(() => {
    pollIntervals.forEach(clearInterval);
    pollIntervals = [];
  });
}

function renderHeaderNav(user: { username: string; role: string } | null): string {
  return `
    <header class="app-header">
      <div class="logo">
        <span class="gradient-text">📸 PhotoViewer</span>
      </div>
      <nav class="header-nav">
        <button class="nav-link" onclick="location.hash='#/gallery'">Gallery</button>
        <button class="nav-link active" onclick="location.hash='#/downloads'">Downloads</button>
        <button class="nav-link" onclick="location.hash='#/duplicates'">Duplicates</button>
        ${user?.role === 'Admin' ? '<button class="nav-link" onclick="location.hash=\'#/users\'">Users</button>' : ''}
      </nav>
      <div class="header-actions">
        <div class="user-badge">👤 ${user?.username || 'User'}</div>
        <button class="btn btn-sm" id="logout-btn">Logout</button>
      </div>
    </header>
  `;
}

async function loadDownloads(): Promise<void> {
  // Since we don't have a "list all downloads" endpoint, we'll track downloads in localStorage
  const downloadIds: number[] = JSON.parse(localStorage.getItem('pv_downloads') || '[]');

  if (downloadIds.length === 0) {
    document.getElementById('downloads-list')!.innerHTML = '';
    document.getElementById('downloads-empty')!.style.display = 'flex';
    return;
  }

  document.getElementById('downloads-empty')!.style.display = 'none';
  const list = document.getElementById('downloads-list')!;
  list.innerHTML = '';

  // Clear old poll intervals
  pollIntervals.forEach(clearInterval);
  pollIntervals = [];

  for (const id of downloadIds.reverse()) {
    try {
      const item = await api.getDownloadStatus(id);
      renderDownloadItem(list, item);

      // Poll pending/processing items
      if (item.status === 'Pending' || item.status === 'Processing') {
        const interval = setInterval(async () => {
          try {
            const updated = await api.getDownloadStatus(id);
            const existing = document.getElementById(`download-${id}`);
            if (existing) {
              existing.outerHTML = getDownloadItemHtml(updated);
              attachDownloadEvents(id, updated);
            }
            if (updated.status === 'Ready' || updated.status === 'Expired') {
              clearInterval(interval);
            }
          } catch { clearInterval(interval); }
        }, 2000);
        pollIntervals.push(interval);
      }
    } catch {
      // Download might have expired
    }
  }
}

function renderDownloadItem(list: HTMLElement, item: DownloadItem): void {
  const div = document.createElement('div');
  div.innerHTML = getDownloadItemHtml(item);
  list.appendChild(div.firstElementChild!);
  attachDownloadEvents(item.id, item);
}

function getDownloadItemHtml(item: DownloadItem): string {
  const statusClass = item.status.toLowerCase();
  return `
    <div class="download-item" id="download-${item.id}">
      <div class="download-info">
        <div class="name">${item.zipFileName || `Download #${item.id}`}</div>
        <div class="meta">
          Created: ${formatDate(item.createdAt)}
          ${item.zipFileSize ? ` · ${formatFileSize(item.zipFileSize)}` : ''}
        </div>
      </div>
      <div style="display:flex;align-items:center;gap:12px">
        <span class="status-badge ${statusClass}">${item.status}</span>
        ${item.status === 'Ready' ? `
          <button class="btn btn-primary btn-sm" data-download="${item.id}" data-filename="${item.zipFileName || 'download.zip'}">
            ⬇ Download
          </button>
        ` : ''}
        <button class="btn btn-sm btn-danger" data-remove="${item.id}" title="Remove">✕</button>
      </div>
    </div>
  `;
}

function attachDownloadEvents(id: number, item: DownloadItem): void {
  document.querySelector(`[data-remove="${id}"]`)?.addEventListener('click', () => {
    const ids: number[] = JSON.parse(localStorage.getItem('pv_downloads') || '[]');
    localStorage.setItem('pv_downloads', JSON.stringify(ids.filter(i => i !== id)));
    document.getElementById(`download-${id}`)?.remove();
  });

  document.querySelector(`[data-download="${id}"]`)?.addEventListener('click', async (e) => {
    const btn = e.currentTarget as HTMLButtonElement;
    const filename = btn.dataset.filename || 'download.zip';
    btn.textContent = '⏳ Downloading...';
    btn.disabled = true;

    try {
      const token = localStorage.getItem('pv_token');
      const res = await fetch(api.getDownloadFileUrl(id), {
        headers: { 'Authorization': `Bearer ${token}` }
      });

      if (!res.ok) throw new Error('Download failed');

      const blob = await res.blob();
      const url = URL.createObjectURL(blob);
      const a = document.createElement('a');
      a.href = url;
      a.download = filename;
      document.body.appendChild(a);
      a.click();
      document.body.removeChild(a);
      URL.revokeObjectURL(url);

      btn.textContent = '✅ Done';
    } catch {
      btn.textContent = '⬇ Download';
      btn.disabled = false;
      showToast('Download failed', 'error');
    }
  });
}

// Helper to record download IDs
export function trackDownloadId(id: number): void {
  const ids: number[] = JSON.parse(localStorage.getItem('pv_downloads') || '[]');
  if (!ids.includes(id)) {
    ids.push(id);
    localStorage.setItem('pv_downloads', JSON.stringify(ids));
  }
}
