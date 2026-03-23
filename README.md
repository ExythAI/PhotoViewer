# 📸 PhotoViewer

A self-hosted photo and video browser for Windows network shares, running in Docker on Linux. Built with **.NET 8** and **TypeScript**.

![Dark Theme](https://img.shields.io/badge/theme-dark-1e293b) ![.NET 8](https://img.shields.io/badge/.NET-8.0-512bd4) ![TypeScript](https://img.shields.io/badge/TypeScript-5.7-3178c6) ![Docker](https://img.shields.io/badge/Docker-ready-2496ed)

---

## Features

- **📂 Multi-Source Browsing** — Add multiple local or network-mounted folders via the Admin page
- **🔍 Full Indexing** — 3-phase background scanner: fast index → thumbnails (6 workers) → checksums (3 workers)
- **🖼 Thumbnail Generation** — Auto-generates WebP thumbnails (ImageSharp for images, FFmpeg for video frames)
- **🔎 Search & Filter** — Search by filename, filter by type (image/video), folder, date range, and sort by date/name/size
- **📱 Responsive** — Mobile-friendly layout with 3 breakpoints (tablet, phone, small phone)
- **⬇ Download Queue** — Select multiple files and download as a single ZIP archive
- **🔐 Authentication** — JWT-based login with rate limiting, role-based access (Admin/User)
- **👥 User Management** — Admin can add and remove users
- **🔁 Duplicate Detection** — SHA-256 checksums on every file to identify duplicates (hidden from gallery by default)
- **🛠 Admin Panel** — Manage media sources, start/stop scans, clear database
- **🌙 Dark Theme** — Glassmorphism UI with smooth animations
- **🎬 Video Player** — In-browser video playback with streaming support
- **⌨ Keyboard Navigation** — Arrow keys and Escape in the detail view

---

## Quick Start

### One-Line Install

```bash
curl -sSL https://raw.githubusercontent.com/ExythAI/PhotoViewer/master/install.sh | bash
```

This will check prerequisites, prompt for your SMB credentials, clone the repo, build the Docker image, and start the container. That's it.

---

### Manual Install

#### Prerequisites

- Docker & Docker Compose on a Linux host
- `cifs-utils` installed on the host (`sudo apt install cifs-utils`)
- Network access to the SMB/CIFS share

### 1. Clone & Configure

```bash
git clone https://github.com/ExythAI/PhotoViewer.git
cd PhotoViewer
cp .env.example .env
```

Edit `.env` with your settings:

```env
SMB_USERNAME=your_username
SMB_PASSWORD=your_password
SCAN_INTERVAL=60
JWT_KEY=YourSecretKeyHere-Min32Characters!!
```

### 2. Build & Run

```bash
docker compose up -d --build
```

### 3. Access

Open **http://your-host:8080** and login with:

| Username | Password |
|----------|----------|
| `admin`  | `admin`  |

> ⚠️ **Change the default password after first login** via Settings (⚙️).

---

## Adding Media Sources (Network Shares)

The scanner needs files to be accessible **inside the Docker container**. This is a 3-step process:

### Step 1: Mount the share on the host

```bash
# Mount a Windows/NAS share
sudo mount -t cifs //winnfs/AllPictures /mnt/AllPictures \
  -o username=USER,password=PASS,vers=3.0,uid=1000,gid=1000

# To make it persistent across reboots, add to /etc/fstab:
# //winnfs/AllPictures  /mnt/AllPictures  cifs  username=USER,password=PASS,vers=3.0,uid=1000,gid=1000,_netdev  0  0
```

### Step 2: Map it into Docker

In `docker-compose.yml`, add volumes under the `photoviewer` service:

```yaml
volumes:
  - /mnt/AllPictures:/media:ro           # Primary source
  - /mnt/OtherPhotos:/media2:ro          # Additional source
  - /mnt/VideoArchive:/media3:ro         # Another source
  - photoviewer-data:/data
```

Then restart:

```bash
docker compose up -d --build
```

### Step 3: Register in the Admin panel

1. Navigate to **Admin** → **Media Sources**
2. Click **+ Add** and enter the container path (e.g. `/media2`)
3. Give it a label (e.g. "Other Photos")
4. Hit **🔄 Scan Now** to index the new source

> The default source (`/media`) is auto-created on first scan. Additional sources must be added manually via the Admin page.

---

## Admin Panel

The Admin page (`#/admin`) is accessible only to Admin users and includes:

| Feature | Description |
|---------|-------------|
| **📡 Scanner Controls** | Start/Stop scans with real-time progress (folders, files, phase, %) |
| **🗑️ Clear Database** | Wipe all indexed media, thumbnails, and downloads (double confirmation) |
| **📁 Media Sources** | Add, remove, enable/disable scan folder paths |

---

## Architecture

```
┌──────────────────────────────────────────────────┐
│                  Docker Container                 │
│                                                   │
│  ┌─────────────┐  ┌───────────────────────────┐  │
│  │  Vite SPA   │  │    .NET 8 Web API          │  │
│  │ (wwwroot)   │──│  ├─ Auth (JWT + BCrypt)    │  │
│  │             │  │  ├─ Media Controller       │  │
│  └─────────────┘  │  ├─ Admin Controller       │  │
│                    │  ├─ Download Controller    │  │
│                    │  ├─ Users Controller       │  │
│                    │  ├─ FileIndexer (bg svc)   │  │
│                    │  ├─ ThumbnailService       │  │
│                    │  └─ DownloadService        │  │
│                    └───────────┬───────────────┘  │
│                                │                   │
│  ┌──────────┐  ┌──────────────┴──────────────┐   │
│  │ SQLite   │  │  /data/thumbnails (WebP)     │   │
│  │ Database │  │  /data/downloads  (ZIP)      │   │
│  └──────────┘  └─────────────────────────────┘   │
│                                                   │
│  /media  ← mount 1 (e.g. \\winnfs\AllPictures)    │
│  /media2 ← mount 2 (e.g. \\winnfs\Videos)         │
└──────────────────────────────────────────────────┘
```

---

## Scanner Phases

The background scanner runs in 3 parallel phases:

| Phase | Description | Workers |
|-------|-------------|---------|
| **1. Fast Index** | Reads file metadata (name, size, date) — no file content reads | Sequential |
| **2. Thumbnails** | Generates WebP thumbnails (ImageSharp / FFmpeg) | 6 parallel |
| **3. Checksums** | Computes SHA-256 hashes for duplicate detection | 3 parallel |

Each phase has per-file error handling with 30-second timeouts for video processing (FFmpeg). The scanner can be stopped mid-scan via the Admin panel — progress is saved.

---

## API Endpoints

| Method | Endpoint | Auth | Description |
|--------|----------|------|-------------|
| `POST` | `/api/auth/login` | — | Login, returns JWT |
| `POST` | `/api/auth/change-password` | ✅ | Change own password |
| `GET` | `/api/media` | ✅ | Paginated media list (search, filter, sort, hideDuplicates) |
| `GET` | `/api/media/{id}` | ✅ | Single file details |
| `GET` | `/api/media/{id}/thumbnail` | ✅ | Serve WebP thumbnail |
| `GET` | `/api/media/{id}/stream` | ✅ | Stream original file |
| `GET` | `/api/media/folders` | ✅ | Folder tree |
| `GET` | `/api/media/duplicates` | ✅ | Files grouped by checksum |
| `GET` | `/api/media/stats` | ✅ | Library statistics |
| `GET` | `/api/media/scan/status` | ✅ | Scanner progress (read-only) |
| `POST` | `/api/admin/scan/start` | Admin | Start scan |
| `POST` | `/api/admin/scan/stop` | Admin | Stop running scan |
| `POST` | `/api/admin/clear` | Admin | Clear all data |
| `GET` | `/api/admin/sources` | Admin | List media sources |
| `POST` | `/api/admin/sources` | Admin | Add media source |
| `DELETE` | `/api/admin/sources/{id}` | Admin | Remove media source |
| `PUT` | `/api/admin/sources/{id}/toggle` | Admin | Enable/disable source |
| `POST` | `/api/download` | ✅ | Create ZIP download request |
| `GET` | `/api/download/{id}` | ✅ | Check download status |
| `GET` | `/api/download/{id}/file` | ✅ | Download the ZIP |
| `GET` | `/api/users` | Admin | List users |
| `POST` | `/api/users` | Admin | Create user |
| `DELETE` | `/api/users/{id}` | Admin | Delete user |

---

## Configuration

All settings can be overridden via environment variables:

| Variable | Default | Description |
|----------|---------|-------------|
| `Storage__MediaPath` | `/media` | Default media source path (auto-seeded) |
| `Storage__DatabasePath` | `/data/photoviewer.db` | SQLite database location |
| `Storage__ThumbnailPath` | `/data/thumbnails` | Generated thumbnails directory |
| `Storage__DownloadPath` | `/data/downloads` | Temporary ZIP files directory |
| `Scanner__IntervalMinutes` | `60` | Background scan interval |
| `Jwt__Key` | *(required)* | JWT signing key (min 32 chars) |

---

## Tech Stack

| Layer | Technology |
|-------|-----------| 
| Backend | .NET 8, ASP.NET Core, EF Core, SQLite |
| Frontend | TypeScript, Vite |
| Thumbnails | SixLabors.ImageSharp, FFmpeg |
| Auth | JWT, BCrypt, Rate Limiting |
| Checksums | SHA-256 |
| Metadata | MetadataExtractor (EXIF) |
| Container | Docker, Docker Compose |

---

## License

MIT
