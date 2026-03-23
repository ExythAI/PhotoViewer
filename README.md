# 📸 PhotoViewer

A self-hosted photo and video browser for Windows network shares, running in Docker on Linux. Built with **.NET 8** and **TypeScript**.

![Dark Theme](https://img.shields.io/badge/theme-dark-1e293b) ![.NET 8](https://img.shields.io/badge/.NET-8.0-512bd4) ![TypeScript](https://img.shields.io/badge/TypeScript-5.7-3178c6) ![Docker](https://img.shields.io/badge/Docker-ready-2496ed)

---

## Features

- **📂 Network Share Browsing** — Mounts `\\winnfs\FamilyPhotos` (or any CIFS/SMB share) via Docker volume
- **🔍 Full Indexing** — Background scanner indexes all images and videos into a searchable SQLite database
- **🖼 Thumbnail Generation** — Auto-generates WebP thumbnails (ImageSharp for images, FFmpeg for video frames)
- **🔎 Search & Filter** — Search by filename, filter by type (image/video), folder, and sort by date/name/size
- **⬇ Download Queue** — Select multiple files and download as a single ZIP archive
- **🔐 Authentication** — JWT-based login with role-based access (Admin/User)
- **👥 User Management** — Admin can add and remove users
- **🔁 Duplicate Detection** — SHA-256 checksums on every file to identify duplicates
- **🌙 Dark Theme** — Glassmorphism UI with smooth animations
- **♾ Infinite Scroll** — Paginated gallery with lazy loading
- **🎬 Video Player** — In-browser video playback with streaming support
- **⌨ Keyboard Navigation** — Arrow keys and Escape in the detail view

---

## Quick Start

### Prerequisites

- Docker & Docker Compose on a Linux host
- `cifs-utils` installed on the host (`sudo apt install cifs-utils`)
- Network access to the SMB/CIFS share

### 1. Clone & Configure

```bash
git clone https://github.com/ExythAI/PhotoViewer.git
cd PhotoViewer
cp .env.example .env
```

Edit `.env` with your network share credentials:

```env
SMB_USERNAME=your_username
SMB_PASSWORD=your_password
SCAN_INTERVAL=60
JWT_KEY=YourSecretKeyHere
```

### 2. Build & Run

```bash
docker-compose up -d --build
```

### 3. Access

Open **http://your-host:8080** and login with:

| Username | Password |
|----------|----------|
| `admin`  | `admin`  |

> ⚠️ **Change the default password after first login.**

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
│  └─────────────┘  │  ├─ Download Controller    │  │
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
│  /media ← CIFS mount (\\winnfs\FamilyPhotos)      │
└──────────────────────────────────────────────────┘
```

---

## Project Structure

```
PhotoViewer/
├── src/
│   ├── PhotoViewer.Api/           # .NET 8 Web API
│   │   ├── Controllers/           # Auth, Media, Download, Users
│   │   ├── Data/                  # EF Core DbContext + Migrations
│   │   ├── Models/                # MediaFile, User, DownloadRequest
│   │   ├── Services/              # FileIndexer, Thumbnail, Download, Auth
│   │   └── Program.cs
│   └── PhotoViewer.Web/           # Vite + TypeScript SPA
│       ├── src/
│       │   ├── pages/             # Login, Gallery, Detail, Downloads, Users, Duplicates
│       │   ├── api/client.ts      # JWT-authenticated fetch wrapper
│       │   ├── styles/theme.css   # Dark theme
│       │   └── main.ts            # Entry + router
│       └── index.html
├── Dockerfile                     # Multi-stage build
├── docker-compose.yml             # CIFS volume mount
└── .env.example                   # Credential template
```

---

## API Endpoints

| Method | Endpoint | Auth | Description |
|--------|----------|------|-------------|
| `POST` | `/api/auth/login` | — | Login, returns JWT |
| `POST` | `/api/auth/change-password` | ✅ | Change own password |
| `GET` | `/api/media` | ✅ | Paginated media list (search, filter, sort) |
| `GET` | `/api/media/{id}` | ✅ | Single file details |
| `GET` | `/api/media/{id}/thumbnail` | ✅ | Serve WebP thumbnail |
| `GET` | `/api/media/{id}/stream` | ✅ | Stream original file |
| `GET` | `/api/media/folders` | ✅ | Folder tree |
| `GET` | `/api/media/duplicates` | ✅ | Files grouped by checksum |
| `GET` | `/api/media/stats` | ✅ | Library statistics |
| `POST` | `/api/media/scan` | Admin | Trigger manual rescan |
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
| `Storage__MediaPath` | `/media` | Path to mounted media files |
| `Storage__DatabasePath` | `/data/photoviewer.db` | SQLite database location |
| `Storage__ThumbnailPath` | `/data/thumbnails` | Generated thumbnails directory |
| `Storage__DownloadPath` | `/data/downloads` | Temporary ZIP files directory |
| `Scanner__IntervalMinutes` | `60` | Background scan interval |
| `Jwt__Key` | *(default key)* | JWT signing key |

---

## Alternative: Host-Mounted Share

If you prefer to mount the share on the host instead of using Docker CIFS volumes:

```bash
# On the host
sudo mount -t cifs //winnfs/FamilyPhotos /mnt/photos -o username=user,password=pass,vers=3.0

# In docker-compose.yml, replace the CIFS volume with:
volumes:
  - /mnt/photos:/media:ro
```

---

## Tech Stack

| Layer | Technology |
|-------|-----------|
| Backend | .NET 8, ASP.NET Core, EF Core, SQLite |
| Frontend | TypeScript, Vite |
| Thumbnails | SixLabors.ImageSharp, FFmpeg |
| Auth | JWT, BCrypt |
| Checksums | SHA-256 |
| Metadata | MetadataExtractor (EXIF) |
| Container | Docker, Docker Compose |

---

## License

MIT
