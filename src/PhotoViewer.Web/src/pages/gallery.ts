import { api, getCurrentUser, clearAuth } from '../api/client';
import { navigate } from '../router';
import { getApp, formatFileSize, formatDate, formatDuration, showToast } from '../utils';
import { renderDetail } from './detail';

interface MediaFile {
  id: number;
  fileName: string;
  relativePath: string;
  extension: string;
  fileSize: number;
  mediaType: string;
  width: number | null;
  height: number | null;
  durationSeconds: number | null;
  takenDate: string | null;
  indexedAt: string;
  hasThumbnail: boolean;
  checksum: string | null;
}

let allItems: MediaFile[] = [];
let currentPage = 1;
let totalCount = 0;
let totalPages = 0;
let isLoading = false;
let selectedIds = new Set<number>();
let searchQuery = '';
let typeFilter = '';
let sortBy = 'takenDate';
let sortDir = 'desc';
let folderFilter = '';
let dateFrom = '';
let dateTo = '';
const PAGE_SIZE = 50;

export function renderGallery(): void {
  const app = getApp();
  const user = getCurrentUser();

  allItems = [];
  currentPage = 1;
  totalCount = 0;
  totalPages = 0;
  selectedIds = new Set();

  app.innerHTML = `
    ${renderHeader(user)}
    <div class="content-area">
      <div id="stats-bar" class="stats-bar"></div>
      <div id="scan-progress" class="scan-progress" style="display:none"></div>
      <div class="gallery-toolbar">
        <div class="search-bar">
          <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
            <circle cx="11" cy="11" r="8"/><path d="M21 21l-4.35-4.35"/>
          </svg>
          <input type="text" class="input" id="search-input" placeholder="Search files..." value="${searchQuery}" />
        </div>
        <div class="filter-group">
          <select class="input" id="type-filter">
            <option value="">All Types</option>
            <option value="Image" ${typeFilter === 'Image' ? 'selected' : ''}>Images</option>
            <option value="Video" ${typeFilter === 'Video' ? 'selected' : ''}>Videos</option>
          </select>
          <select class="input" id="sort-select">
            <option value="takenDate-desc" ${sortBy === 'takenDate' && sortDir === 'desc' ? 'selected' : ''}>Newest First</option>
            <option value="takenDate-asc" ${sortBy === 'takenDate' && sortDir === 'asc' ? 'selected' : ''}>Oldest First</option>
            <option value="name-asc" ${sortBy === 'name' && sortDir === 'asc' ? 'selected' : ''}>Name A-Z</option>
            <option value="name-desc" ${sortBy === 'name' && sortDir === 'desc' ? 'selected' : ''}>Name Z-A</option>
            <option value="size-desc" ${sortBy === 'size' && sortDir === 'desc' ? 'selected' : ''}>Largest First</option>
            <option value="size-asc" ${sortBy === 'size' && sortDir === 'asc' ? 'selected' : ''}>Smallest First</option>
          </select>
          <select class="input" id="folder-filter">
            <option value="">All Folders</option>
          </select>
        </div>
      </div>
      <div class="gallery-toolbar" style="padding-top:0">
        <div class="filter-group">
          <label style="font-size:13px;color:var(--text-muted);white-space:nowrap">Date Range:</label>
          <input type="date" class="input" id="date-from" value="${dateFrom}" />
          <span style="color:var(--text-muted)">to</span>
          <input type="date" class="input" id="date-to" value="${dateTo}" />
          <button class="btn btn-sm" id="clear-dates" style="font-size:12px" ${!dateFrom && !dateTo ? 'style="display:none"' : ''}>✕ Clear</button>
        </div>
      </div>
      <div id="media-grid" class="media-grid"></div>
      <div id="loading" class="loading-spinner" style="display:none"></div>
      <div id="pagination" class="pagination" style="display:none"></div>
      <div id="gallery-empty" class="gallery-empty" style="display:none">
        <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.5">
          <rect x="3" y="3" width="18" height="18" rx="2"/><circle cx="8.5" cy="8.5" r="1.5"/>
          <path d="M21 15l-5-5L5 21"/>
        </svg>
        <h3>No media found</h3>
        <p>Try adjusting your filters or wait for the scanner to index your files.</p>
      </div>
    </div>
    <div id="selection-bar" class="selection-bar" style="display:none">
      <span class="count" id="selection-count">0 selected</span>
      <button class="btn btn-primary btn-sm" id="download-selected">
        ⬇ Download as ZIP
      </button>
      <button class="btn btn-sm" id="clear-selection">✕ Clear</button>
    </div>
  `;

  setupEventListeners();
  loadStats();
  loadFolders();
  loadMedia();
  pollScanStatus();
}

function renderHeader(user: { username: string; role: string } | null): string {
  return `
    <header class="app-header">
      <div class="logo">
        <span class="gradient-text">📸 PhotoViewer</span>
      </div>
      <nav class="header-nav">
        <button class="nav-link active" onclick="location.hash='#/gallery'">Gallery</button>
        <button class="nav-link" onclick="location.hash='#/downloads'">Downloads</button>
        <button class="nav-link" onclick="location.hash='#/duplicates'">Duplicates</button>
        ${user?.role === 'Admin' ? '<button class="nav-link" onclick="location.hash=\'#/users\'">Users</button>' : ''}
      </nav>
      <div class="header-actions">
        ${user?.role === 'Admin' ? '<button class="btn btn-sm" id="scan-btn">🔄 Scan Now</button>' : ''}
        <div class="user-badge">👤 ${user?.username || 'User'}</div>
        <button class="btn btn-sm" onclick="location.hash='#/settings'">⚙️</button>
        <button class="btn btn-sm" id="logout-btn">Logout</button>
      </div>
    </header>
  `;
}

let scanPollTimer: ReturnType<typeof setInterval> | null = null;
let wasScanning = false;

function pollScanStatus(): void {
  // Clear any previous timer
  if (scanPollTimer) clearInterval(scanPollTimer);

  const update = async () => {
    const el = document.getElementById('scan-progress');
    if (!el) {
      if (scanPollTimer) clearInterval(scanPollTimer);
      return;
    }

    try {
      const s = await api.getScanStatus();

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
            <span>📁 Folders: ${s.scannedFolders}/${s.totalFolders}</span>
            <span>📄 Files: ${s.processedFiles.toLocaleString()}/${s.totalFiles.toLocaleString()}</span>
            <span>🆕 New: ${s.newFiles}</span>
            <span>✏️ Updated: ${s.updatedFiles}</span>
            ${s.currentFile ? `<span>📎 ${s.currentFile}</span>` : ''}
          </div>
        `;
      } else {
        if (wasScanning) {
          // Scan just finished — refresh gallery data
          wasScanning = false;
          el.style.display = 'block';
          el.innerHTML = `
            <div class="scan-progress-header">
              <span class="scan-status-label">✅ Scan complete</span>
              <span class="scan-percent">100%</span>
            </div>
            <div class="scan-bar-track">
              <div class="scan-bar-fill" style="width:100%"></div>
            </div>
            <div class="scan-details">
              <span>🆕 New: ${s.newFiles}</span>
              <span>✏️ Updated: ${s.updatedFiles}</span>
              <span>🗑️ Deleted: ${s.deletedFiles}</span>
            </div>
          `;
          // Refresh gallery data
          loadStats();
          loadMedia();
          // Hide after 10 seconds
          setTimeout(() => {
            if (el) el.style.display = 'none';
          }, 10000);
        } else {
          el.style.display = 'none';
        }
      }
    } catch {}
  };

  update();
  scanPollTimer = setInterval(update, 2000);
}

function setupEventListeners(): void {
  let searchTimeout: ReturnType<typeof setTimeout>;
  document.getElementById('search-input')?.addEventListener('input', (e) => {
    clearTimeout(searchTimeout);
    searchTimeout = setTimeout(() => {
      searchQuery = (e.target as HTMLInputElement).value;
      currentPage = 1;
      loadMedia();
    }, 300);
  });

  document.getElementById('type-filter')?.addEventListener('change', (e) => {
    typeFilter = (e.target as HTMLSelectElement).value;
    currentPage = 1;
    loadMedia();
  });

  document.getElementById('sort-select')?.addEventListener('change', (e) => {
    const val = (e.target as HTMLSelectElement).value;
    const [s, d] = val.split('-');
    sortBy = s;
    sortDir = d;
    currentPage = 1;
    loadMedia();
  });

  document.getElementById('folder-filter')?.addEventListener('change', (e) => {
    folderFilter = (e.target as HTMLSelectElement).value;
    currentPage = 1;
    loadMedia();
  });

  document.getElementById('date-from')?.addEventListener('change', (e) => {
    dateFrom = (e.target as HTMLInputElement).value;
    currentPage = 1;
    loadMedia();
  });

  document.getElementById('date-to')?.addEventListener('change', (e) => {
    dateTo = (e.target as HTMLInputElement).value;
    currentPage = 1;
    loadMedia();
  });

  document.getElementById('clear-dates')?.addEventListener('click', () => {
    dateFrom = '';
    dateTo = '';
    (document.getElementById('date-from') as HTMLInputElement).value = '';
    (document.getElementById('date-to') as HTMLInputElement).value = '';
    currentPage = 1;
    loadMedia();
  });

  document.getElementById('download-selected')?.addEventListener('click', downloadSelected);
  document.getElementById('clear-selection')?.addEventListener('click', () => {
    selectedIds.clear();
    updateSelectionUI();
    document.querySelectorAll('.media-card.selected').forEach(c => c.classList.remove('selected'));
  });

  document.getElementById('logout-btn')?.addEventListener('click', () => {
    clearAuth();
    navigate('/login');
  });

  document.getElementById('scan-btn')?.addEventListener('click', async () => {
    try {
      await api.triggerScan();
      showToast('Scan started!');
      wasScanning = false; // Reset so we detect the new scan
      pollScanStatus();
    } catch {
      showToast('Failed to start scan', 'error');
    }
  });
}

async function loadStats(): Promise<void> {
  try {
    const stats = await api.getStats();
    document.getElementById('stats-bar')!.innerHTML = `
      <div class="stat-card"><div class="stat-value">${stats.totalFiles.toLocaleString()}</div><div class="stat-label">Total Files</div></div>
      <div class="stat-card"><div class="stat-value">${stats.totalImages.toLocaleString()}</div><div class="stat-label">Images</div></div>
      <div class="stat-card"><div class="stat-value">${stats.totalVideos.toLocaleString()}</div><div class="stat-label">Videos</div></div>
      <div class="stat-card"><div class="stat-value">${formatFileSize(stats.totalSize)}</div><div class="stat-label">Total Size</div></div>
      <div class="stat-card"><div class="stat-value">${stats.duplicateGroups}</div><div class="stat-label">Duplicate Groups</div></div>
    `;
  } catch {}
}

async function loadFolders(): Promise<void> {
  try {
    const folders = await api.getFolders();
    const select = document.getElementById('folder-filter') as HTMLSelectElement;
    folders.forEach(f => {
      const opt = document.createElement('option');
      opt.value = f;
      opt.textContent = f;
      if (f === folderFilter) opt.selected = true;
      select.appendChild(opt);
    });
  } catch {}
}

async function loadMedia(): Promise<void> {
  if (isLoading) return;
  isLoading = true;
  document.getElementById('loading')!.style.display = 'flex';
  document.getElementById('media-grid')!.innerHTML = '';

  try {
    const result = await api.getMedia({
      search: searchQuery || undefined,
      folder: folderFilter || undefined,
      type: typeFilter || undefined,
      dateFrom: dateFrom || undefined,
      dateTo: dateTo || undefined,
      sortBy,
      sortDir,
      page: currentPage,
      pageSize: PAGE_SIZE,
    });

    totalCount = result.totalCount;
    totalPages = Math.ceil(totalCount / PAGE_SIZE);
    allItems = result.items;

    renderMediaItems(result.items);
    renderPagination();

    document.getElementById('gallery-empty')!.style.display =
      allItems.length === 0 ? 'flex' : 'none';
  } catch (err) {
    showToast('Failed to load media', 'error');
  } finally {
    isLoading = false;
    document.getElementById('loading')!.style.display = 'none';
  }
}

function renderMediaItems(items: MediaFile[]): void {
  const grid = document.getElementById('media-grid')!;

  items.forEach(item => {
    const card = document.createElement('div');
    card.className = `media-card${selectedIds.has(item.id) ? ' selected' : ''}`;
    card.dataset.id = item.id.toString();

    const thumbUrl = item.hasThumbnail ? api.getThumbnailUrl(item.id) : '';
    const isVideo = item.mediaType === 'Video';

    card.innerHTML = `
      <div class="select-checkbox" data-action="select"></div>
      ${isVideo ? `<span class="video-badge">▶ ${formatDuration(item.durationSeconds)}</span>` : ''}
      ${thumbUrl
        ? `<img src="${thumbUrl}" alt="${item.fileName}" loading="lazy" 
             onerror="this.style.display='none';this.nextElementSibling.style.display='flex'" />
           <div class="media-placeholder" style="display:none">${isVideo ? '🎬' : '🖼'}</div>`
        : `<div class="media-placeholder">${isVideo ? '🎬' : '🖼'}</div>`
      }
      <div class="media-card-overlay">
        <div class="filename">${item.fileName}</div>
        <div class="file-meta">${formatFileSize(item.fileSize)} · ${formatDate(item.takenDate)}</div>
      </div>
    `;

    card.addEventListener('click', (e) => {
      const target = e.target as HTMLElement;
      if (target.closest('[data-action="select"]')) {
        toggleSelection(item.id, card);
        return;
      }
      openDetail(item.id);
    });

    grid.appendChild(card);
  });
}

function renderPagination(): void {
  const pag = document.getElementById('pagination')!;

  if (totalPages <= 1) {
    pag.style.display = 'none';
    return;
  }

  pag.style.display = 'flex';

  const startItem = (currentPage - 1) * PAGE_SIZE + 1;
  const endItem = Math.min(currentPage * PAGE_SIZE, totalCount);

  let buttons = '';

  // Previous
  buttons += `<button class="page-btn" ${currentPage <= 1 ? 'disabled' : ''} data-page="${currentPage - 1}">‹ Prev</button>`;

  // Page numbers
  const maxVisible = 7;
  let startPage = Math.max(1, currentPage - Math.floor(maxVisible / 2));
  let endPage = Math.min(totalPages, startPage + maxVisible - 1);
  if (endPage - startPage < maxVisible - 1) {
    startPage = Math.max(1, endPage - maxVisible + 1);
  }

  if (startPage > 1) {
    buttons += `<button class="page-btn" data-page="1">1</button>`;
    if (startPage > 2) buttons += `<span class="page-dots">…</span>`;
  }

  for (let i = startPage; i <= endPage; i++) {
    buttons += `<button class="page-btn${i === currentPage ? ' active' : ''}" data-page="${i}">${i}</button>`;
  }

  if (endPage < totalPages) {
    if (endPage < totalPages - 1) buttons += `<span class="page-dots">…</span>`;
    buttons += `<button class="page-btn" data-page="${totalPages}">${totalPages}</button>`;
  }

  // Next
  buttons += `<button class="page-btn" ${currentPage >= totalPages ? 'disabled' : ''} data-page="${currentPage + 1}">Next ›</button>`;

  pag.innerHTML = `
    <span class="page-info">Showing ${startItem}–${endItem} of ${totalCount.toLocaleString()}</span>
    <div class="page-buttons">${buttons}</div>
  `;

  // Attach click handlers
  pag.querySelectorAll('.page-btn:not([disabled])').forEach(btn => {
    btn.addEventListener('click', () => {
      const page = parseInt((btn as HTMLElement).dataset.page || '1');
      if (page !== currentPage) {
        currentPage = page;
        loadMedia();
        window.scrollTo({ top: 0, behavior: 'smooth' });
      }
    });
  });
}

function toggleSelection(id: number, card: HTMLElement): void {
  if (selectedIds.has(id)) {
    selectedIds.delete(id);
    card.classList.remove('selected');
  } else {
    selectedIds.add(id);
    card.classList.add('selected');
  }
  updateSelectionUI();
}

function updateSelectionUI(): void {
  const bar = document.getElementById('selection-bar')!;
  const count = document.getElementById('selection-count')!;
  if (selectedIds.size > 0) {
    bar.style.display = 'flex';
    count.textContent = `${selectedIds.size} selected`;
  } else {
    bar.style.display = 'none';
  }
}

function openDetail(id: number): void {
  const idx = allItems.findIndex(i => i.id === id);
  renderDetail(allItems, idx >= 0 ? idx : 0);
}

async function downloadSelected(): Promise<void> {
  if (selectedIds.size === 0) return;

  try {
    const result = await api.createDownload(Array.from(selectedIds));
    showToast(`Download request created! ID: ${result.id}`);
    navigate('/downloads');
  } catch (err: any) {
    showToast(err.message || 'Failed to create download', 'error');
  }
}

// Re-export for header navigation
export { renderHeader };
