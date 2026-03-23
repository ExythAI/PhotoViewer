import { api, getCurrentUser, clearAuth } from '../api/client';
import { navigate } from '../router';
import { getApp, formatFileSize, formatDate, showToast } from '../utils';

interface DuplicateGroup {
  checksum: string;
  count: number;
  files: {
    id: number;
    fileName: string;
    relativePath: string;
    fileSize: number;
    mediaType: string;
    hasThumbnail: boolean;
  }[];
}

export function renderDuplicates(): void {
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
        <button class="nav-link active" onclick="location.hash='#/duplicates'">Duplicates</button>
        ${user?.role === 'Admin' ? '<button class="nav-link" onclick="location.hash=\'#/users\'">Users</button>' : ''}
      </nav>
      <div class="header-actions">
        <div class="user-badge">👤 ${user?.username || 'User'}</div>
        <button class="btn btn-sm" id="logout-btn">Logout</button>
      </div>
    </header>
    <div class="content-area">
      <div class="page-header">
        <h2>Duplicate Files</h2>
      </div>
      <div id="duplicates-container">
        <div class="loading-spinner"></div>
      </div>
      <div id="duplicates-empty" class="gallery-empty" style="display:none">
        <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.5">
          <path d="M16 16v4a2 2 0 01-2 2H6a2 2 0 01-2-2V10a2 2 0 012-2h4"/>
          <rect x="10" y="2" width="12" height="12" rx="2"/>
        </svg>
        <h3>No duplicates found</h3>
        <p>All files have unique checksums. No duplicates detected.</p>
      </div>
    </div>
  `;

  document.getElementById('logout-btn')?.addEventListener('click', () => {
    clearAuth();
    navigate('/login');
  });

  loadDuplicates();
}

async function loadDuplicates(): Promise<void> {
  const container = document.getElementById('duplicates-container')!;

  try {
    const groups: DuplicateGroup[] = await api.getDuplicates();

    if (groups.length === 0) {
      container.innerHTML = '';
      document.getElementById('duplicates-empty')!.style.display = 'flex';
      return;
    }

    container.innerHTML = '';
    document.getElementById('duplicates-empty')!.style.display = 'none';

    groups.forEach(group => {
      const div = document.createElement('div');
      div.className = 'duplicate-group';
      div.innerHTML = `
        <div class="duplicate-group-header">
          <span style="font-weight:600;color:var(--text-accent)">${group.count} copies</span>
          <span class="checksum">SHA-256: ${group.checksum?.substring(0, 24)}…</span>
          <span style="font-size:12px;color:var(--text-muted)">${formatFileSize(group.files[0]?.fileSize || 0)} each</span>
        </div>
        <div class="duplicate-files">
          ${group.files.map(f => `
            <div class="media-card" style="aspect-ratio:auto;cursor:pointer" onclick="location.hash='#/gallery'">
              ${f.hasThumbnail
                ? `<img src="${api.getThumbnailUrl(f.id)}" alt="${f.fileName}" style="width:100%;height:120px;object-fit:cover" />`
                : `<div class="media-placeholder" style="height:120px">${f.mediaType === 'Video' ? '🎬' : '🖼'}</div>`
              }
              <div style="padding:8px">
                <div style="font-size:12px;font-weight:500;white-space:nowrap;overflow:hidden;text-overflow:ellipsis">${f.fileName}</div>
                <div style="font-size:11px;color:var(--text-muted);white-space:nowrap;overflow:hidden;text-overflow:ellipsis">${f.relativePath}</div>
              </div>
            </div>
          `).join('')}
        </div>
      `;
      container.appendChild(div);
    });
  } catch (err) {
    container.innerHTML = '<p style="color:var(--text-muted)">Failed to load duplicates</p>';
    showToast('Failed to load duplicates', 'error');
  }
}
