import { api, getCurrentUser, clearAuth } from '../api/client';
import { navigate, setCleanup } from '../router';
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
let isLoading = false;
let hasMore = true;
let selectedIds = new Set<number>();
let searchQuery = '';
let typeFilter = '';
let sortBy = 'takenDate';
let sortDir = 'desc';
let folderFilter = '';
let observer: IntersectionObserver | null = null;

export function renderGallery(): void {
  const app = getApp();
  const user = getCurrentUser();

  allItems = [];
  currentPage = 1;
  totalCount = 0;
  hasMore = true;
  selectedIds = new Set();

  app.innerHTML = `
    ${renderHeader(user)}
    <div class="content-area">
      <div id="stats-bar" class="stats-bar"></div>
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
      <div id="media-grid" class="media-grid"></div>
      <div id="loading" class="loading-spinner" style="display:none"></div>
      <div id="load-more" class="load-more-trigger"></div>
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

  // Infinite scroll observer
  const trigger = document.getElementById('load-more')!;
  observer = new IntersectionObserver((entries) => {
    if (entries[0].isIntersecting && hasMore && !isLoading) {
      loadMedia();
    }
  }, { rootMargin: '200px' });
  observer.observe(trigger);

  setCleanup(() => {
    if (observer) {
      observer.disconnect();
      observer = null;
    }
  });
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
        <button class="btn btn-sm" id="logout-btn">Logout</button>
      </div>
    </header>
  `;
}

function setupEventListeners(): void {
  let searchTimeout: ReturnType<typeof setTimeout>;
  document.getElementById('search-input')?.addEventListener('input', (e) => {
    clearTimeout(searchTimeout);
    searchTimeout = setTimeout(() => {
      searchQuery = (e.target as HTMLInputElement).value;
      resetAndReload();
    }, 300);
  });

  document.getElementById('type-filter')?.addEventListener('change', (e) => {
    typeFilter = (e.target as HTMLSelectElement).value;
    resetAndReload();
  });

  document.getElementById('sort-select')?.addEventListener('change', (e) => {
    const val = (e.target as HTMLSelectElement).value;
    const [s, d] = val.split('-');
    sortBy = s;
    sortDir = d;
    resetAndReload();
  });

  document.getElementById('folder-filter')?.addEventListener('change', (e) => {
    folderFilter = (e.target as HTMLSelectElement).value;
    resetAndReload();
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
    } catch {
      showToast('Failed to start scan', 'error');
    }
  });
}

function resetAndReload(): void {
  allItems = [];
  currentPage = 1;
  hasMore = true;
  document.getElementById('media-grid')!.innerHTML = '';
  loadMedia();
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
  if (isLoading || !hasMore) return;
  isLoading = true;
  document.getElementById('loading')!.style.display = 'flex';

  try {
    const result = await api.getMedia({
      search: searchQuery || undefined,
      folder: folderFilter || undefined,
      type: typeFilter || undefined,
      sortBy,
      sortDir,
      page: currentPage,
      pageSize: 50,
    });

    totalCount = result.totalCount;
    allItems.push(...result.items);
    currentPage++;
    hasMore = allItems.length < totalCount;

    renderMediaItems(result.items);

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
