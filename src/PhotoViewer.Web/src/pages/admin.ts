import { api, getCurrentUser, clearAuth } from '../api/client';
import { navigate } from '../router';
import { getApp, showToast } from '../utils';

interface MediaSource {
  id: number;
  path: string;
  label: string;
  isActive: boolean;
  createdAt: string;
}

export function renderAdmin(): void {
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
        <button class="nav-link" onclick="location.hash='#/users'">Users</button>
        <button class="nav-link active" onclick="location.hash='#/admin'">Admin</button>
      </nav>
      <div class="header-actions">
        <div class="user-badge">👤 ${user?.username || 'User'}</div>
        <button class="btn btn-sm" onclick="location.hash='#/settings'">⚙️</button>
        <button class="btn btn-sm" id="logout-btn">Logout</button>
      </div>
    </header>
    <div class="content-area">
      <h2 style="margin-bottom:20px">🛠️ Admin Panel</h2>

      <!-- Scan Controls -->
      <div class="card admin-section">
        <div class="card-header"><h3>📡 Scanner Controls</h3></div>
        <div class="card-body">
          <div id="scan-progress" class="scan-progress" style="display:none"></div>
          <div class="admin-btn-row">
            <button class="btn btn-primary" id="scan-btn">🔄 Scan Now</button>
            <button class="btn btn-danger" id="clear-db-btn">🗑️ Clear Database</button>
          </div>
        </div>
      </div>

      <!-- Media Sources -->
      <div class="card admin-section">
        <div class="card-header"><h3>📁 Media Sources</h3></div>
        <div class="card-body">
          <div id="sources-list" class="sources-list"></div>
          <div class="add-source-form">
            <div class="input-row">
              <input class="input" id="source-path" placeholder="Path (e.g. /media/photos)" style="flex:2" />
              <input class="input" id="source-label" placeholder="Label (optional)" style="flex:1" />
              <button class="btn btn-primary" id="add-source-btn">+ Add</button>
            </div>
            <p class="input-hint">Add local or mounted folders the scanner should index. Paths must be accessible inside the Docker container.</p>
          </div>
        </div>
      </div>
    </div>
  `;

  document.getElementById('logout-btn')?.addEventListener('click', () => {
    clearAuth();
    navigate('/login');
  });

  // ── Scan controls ──
  let scanPollTimer: ReturnType<typeof setInterval> | null = null;
  let wasScanning = false;

  async function pollScanStatus(): Promise<void> {
    if (scanPollTimer) clearInterval(scanPollTimer);

    const update = async () => {
      try {
        const s = await api.getScanStatus();
        const el = document.getElementById('scan-progress');
        if (!el) return;

        if (s.isScanning) {
          wasScanning = true;
          el.style.display = 'block';
          el.innerHTML = `
            <div class="scan-progress-header">
              <span class="scan-status-label">🔄 ${s.status}</span>
              <span class="scan-percent">${s.percentComplete}%</span>
            </div>
            <div class="scan-bar-track">
              <div class="scan-bar-fill" style="width:${s.percentComplete}%"></div>
            </div>
            <div class="scan-details">
              <span>📁 ${s.scannedFolders}/${s.totalFolders} folders</span>
              <span>📄 ${s.processedFiles.toLocaleString()}/${s.totalFiles.toLocaleString()} files</span>
              <span>🆕 ${s.newFiles} new</span>
              <span>✏️ ${s.updatedFiles} updated</span>
              ${s.skippedFiles ? `<span>⚠️ ${s.skippedFiles} skipped</span>` : ''}
              ${s.currentFile ? `<span>📎 ${s.currentFile}</span>` : ''}
            </div>
          `;
          const scanBtn = document.getElementById('scan-btn');
          if (scanBtn) {
            scanBtn.textContent = '⏹ Stop Scan';
            scanBtn.classList.add('btn-danger');
            scanBtn.classList.remove('btn-primary');
          }
        } else {
          const scanBtn = document.getElementById('scan-btn');
          if (scanBtn) {
            scanBtn.textContent = '🔄 Scan Now';
            scanBtn.classList.add('btn-primary');
            scanBtn.classList.remove('btn-danger');
          }
          if (wasScanning) {
            wasScanning = false;
            el.innerHTML = `<div class="scan-progress-header"><span class="scan-status-label">✅ Scan complete</span></div>`;
            el.style.display = 'block';
            setTimeout(() => { if (el) el.style.display = 'none'; }, 5000);
          } else {
            el.style.display = 'none';
          }
        }
      } catch {}
    };

    update();
    scanPollTimer = setInterval(update, 2000);
  }

  document.getElementById('scan-btn')?.addEventListener('click', async () => {
    const btn = document.getElementById('scan-btn')!;
    const isScanning = btn.textContent?.includes('Stop');

    if (isScanning) {
      try {
        await api.adminStopScan();
        showToast('Stop requested');
      } catch(e: any) {
        showToast(e.message || 'Failed to stop scan', 'error');
      }
    } else {
      try {
        await api.adminStartScan();
        showToast('Scan started!');
        wasScanning = false;
        pollScanStatus();
      } catch(e: any) {
        showToast(e.message || 'Failed to start scan', 'error');
      }
    }
  });

  document.getElementById('clear-db-btn')?.addEventListener('click', async () => {
    if (!confirm('⚠️ This will DELETE all indexed media, thumbnails, and downloads.\n\nAre you sure?')) return;
    if (!confirm('This cannot be undone. Click OK to confirm.')) return;

    try {
      const result = await api.adminClearDatabase();
      showToast(result.message);
    } catch (err: any) {
      showToast(err.message || 'Failed to clear database', 'error');
    }
  });

  // ── Media Sources ──
  async function loadSources(): Promise<void> {
    const container = document.getElementById('sources-list')!;
    try {
      const sources: MediaSource[] = await api.adminGetSources();

      if (sources.length === 0) {
        container.innerHTML = '<p class="text-muted">No media sources configured.</p>';
        return;
      }

      container.innerHTML = sources.map(s => `
        <div class="source-card ${s.isActive ? '' : 'source-inactive'}">
          <div class="source-info">
            <span class="source-label">${s.isActive ? '🟢' : '🔴'} ${s.label}</span>
            <span class="source-path">${s.path}</span>
          </div>
          <div class="source-actions">
            <button class="btn btn-sm" data-toggle="${s.id}">${s.isActive ? 'Disable' : 'Enable'}</button>
            <button class="btn btn-sm btn-danger-outline" data-delete="${s.id}">Remove</button>
          </div>
        </div>
      `).join('');

      container.querySelectorAll('[data-toggle]').forEach(btn => {
        btn.addEventListener('click', async () => {
          const id = (btn as HTMLElement).dataset.toggle!;
          try {
            await api.adminToggleSource(parseInt(id));
            loadSources();
          } catch(e: any) { showToast(e.message || 'Failed', 'error'); }
        });
      });

      container.querySelectorAll('[data-delete]').forEach(btn => {
        btn.addEventListener('click', async () => {
          const id = (btn as HTMLElement).dataset.delete!;
          if (!confirm('Remove this media source?')) return;
          try {
            await api.adminDeleteSource(parseInt(id));
            loadSources();
            showToast('Source removed');
          } catch(e: any) { showToast(e.message || 'Failed', 'error'); }
        });
      });
    } catch {
      container.innerHTML = '<p class="text-muted">Failed to load sources.</p>';
    }
  }

  document.getElementById('add-source-btn')?.addEventListener('click', async () => {
    const pathInput = document.getElementById('source-path') as HTMLInputElement;
    const labelInput = document.getElementById('source-label') as HTMLInputElement;

    const path = pathInput.value.trim();
    if (!path) {
      showToast('Path is required', 'error');
      return;
    }

    try {
      await api.adminAddSource(path, labelInput.value.trim() || undefined);
      pathInput.value = '';
      labelInput.value = '';
      showToast('Source added');
      loadSources();
    } catch (err: any) {
      showToast(err.message || 'Failed to add source', 'error');
    }
  });

  loadSources();
  pollScanStatus();
}
