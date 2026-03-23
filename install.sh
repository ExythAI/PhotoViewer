#!/bin/bash
# ============================================================
#  PhotoViewer — One-Step Docker Install Script
#  Usage:  curl -sSL https://raw.githubusercontent.com/ExythAI/PhotoViewer/master/install.sh | bash
#     or:  ./install.sh
# ============================================================

set -e

CYAN='\033[0;36m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
RED='\033[0;31m'
NC='\033[0m'
BOLD='\033[1m'

INSTALL_DIR="${INSTALL_DIR:-/opt/photoviewer}"

echo ""
echo -e "${CYAN}${BOLD}📸 PhotoViewer Installer${NC}"
echo -e "${CYAN}━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━${NC}"
echo ""

# ---- Pre-flight checks ----
check_command() {
    if ! command -v "$1" &> /dev/null; then
        echo -e "${RED}✗ $1 is not installed.${NC}"
        return 1
    fi
    echo -e "${GREEN}✓${NC} $1 found"
    return 0
}

echo -e "${BOLD}Checking prerequisites...${NC}"
MISSING=0
check_command docker || MISSING=1
check_command git || MISSING=1

# Detect docker compose (plugin) vs docker-compose (standalone)
COMPOSE_CMD=""
if command -v docker-compose &> /dev/null; then
    COMPOSE_CMD="docker-compose"
    echo -e "${GREEN}✓${NC} docker-compose found"
elif docker compose version &> /dev/null 2>&1; then
    COMPOSE_CMD="docker compose"
    echo -e "${GREEN}✓${NC} docker compose (plugin) found"
else
    echo -e "${YELLOW}⚠ docker compose not found — installing plugin...${NC}"
    sudo apt-get update -qq && sudo apt-get install -y -qq docker-compose-plugin 2>/dev/null \
      || sudo mkdir -p /usr/local/lib/docker/cli-plugins \
      && sudo curl -SL "https://github.com/docker/compose/releases/latest/download/docker-compose-linux-$(uname -m)" \
         -o /usr/local/lib/docker/cli-plugins/docker-compose \
      && sudo chmod +x /usr/local/lib/docker/cli-plugins/docker-compose

    if docker compose version &> /dev/null 2>&1; then
        COMPOSE_CMD="docker compose"
        echo -e "${GREEN}✓${NC} docker compose plugin installed"
    else
        echo -e "${RED}✗ Failed to install docker compose plugin.${NC}"
        MISSING=1
    fi
fi

if [ "$MISSING" -eq 1 ]; then
    echo ""
    echo -e "${RED}Please install the missing prerequisites and re-run this script.${NC}"
    exit 1
fi

# Check if cifs-utils is available (needed for SMB mount)
if ! dpkg -s cifs-utils &> /dev/null 2>&1; then
    echo -e "${YELLOW}⚠ cifs-utils not found — installing...${NC}"
    sudo apt-get update -qq && sudo apt-get install -y -qq cifs-utils
    echo -e "${GREEN}✓${NC} cifs-utils installed"
else
    echo -e "${GREEN}✓${NC} cifs-utils found"
fi

echo ""

# ---- Gather configuration ----
echo -e "${BOLD}Network Share Configuration${NC}"
echo -e "${CYAN}━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━${NC}"

read -p "SMB Share path [//winnfs/FamilyPhotos]: " SMB_SHARE
SMB_SHARE="${SMB_SHARE:-//winnfs/FamilyPhotos}"

read -p "SMB Username: " SMB_USERNAME
read -s -p "SMB Password: " SMB_PASSWORD
echo ""

echo ""
echo -e "${BOLD}Application Settings${NC}"
echo -e "${CYAN}━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━${NC}"

read -p "Port [8080]: " APP_PORT
APP_PORT="${APP_PORT:-8080}"

read -p "Scan interval in minutes [60]: " SCAN_INTERVAL
SCAN_INTERVAL="${SCAN_INTERVAL:-60}"

read -p "Install directory [${INSTALL_DIR}]: " INPUT_DIR
INSTALL_DIR="${INPUT_DIR:-$INSTALL_DIR}"

echo ""

# ---- Clone / Update repo ----
echo -e "${BOLD}Setting up PhotoViewer...${NC}"
echo -e "${CYAN}━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━${NC}"

if [ -d "$INSTALL_DIR/.git" ]; then
    echo -e "${YELLOW}→${NC} Existing installation found, pulling latest..."
    cd "$INSTALL_DIR"
    git checkout -- . 2>/dev/null
    git pull --ff-only
else
    echo -e "${YELLOW}→${NC} Cloning repository..."
    sudo mkdir -p "$INSTALL_DIR"
    sudo chown "$USER:$USER" "$INSTALL_DIR"
    git clone https://github.com/ExythAI/PhotoViewer.git "$INSTALL_DIR"
    cd "$INSTALL_DIR"
fi

echo -e "${GREEN}✓${NC} Source ready"

# ---- Generate .env ----
JWT_KEY=$(openssl rand -base64 48 2>/dev/null | tr -d '\r\n' || head -c 64 /dev/urandom | base64 | tr -d '\r\n')

printf "SMB_SHARE=%s\nSMB_USERNAME=%s\nSMB_PASSWORD=%s\nAPP_PORT=%s\nSCAN_INTERVAL=%s\nJWT_KEY=%s\n" \
  "$SMB_SHARE" "$SMB_USERNAME" "$SMB_PASSWORD" "$APP_PORT" "$SCAN_INTERVAL" "$JWT_KEY" > .env

chmod 600 .env
echo -e "${GREEN}✓${NC} Configuration saved (.env)"

# ---- Build & Start ----
echo ""
echo -e "${BOLD}Building and starting containers...${NC}"
echo -e "${CYAN}━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━${NC}"
echo -e "${YELLOW}(this may take a few minutes on first run)${NC}"
echo ""

# Strip any Windows CRLF line endings from config files
sed -i 's/\r$//' docker-compose.yml .env 2>/dev/null

$COMPOSE_CMD up -d --build

echo ""
echo -e "${GREEN}${BOLD}━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━${NC}"
echo -e "${GREEN}${BOLD}✅ PhotoViewer is running!${NC}"
echo -e "${GREEN}${BOLD}━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━${NC}"
echo ""
echo -e "   🌐  URL:       ${BOLD}http://$(hostname -I | awk '{print $1}'):${APP_PORT}${NC}"
echo -e "   👤  Login:     ${BOLD}admin${NC} / ${BOLD}admin${NC}"
echo -e "   📂  Share:     ${BOLD}${SMB_SHARE}${NC}"
echo -e "   🔄  Scanning:  every ${BOLD}${SCAN_INTERVAL}${NC} minutes"
echo -e "   📁  Installed: ${BOLD}${INSTALL_DIR}${NC}"
echo ""
echo -e "   ${YELLOW}⚠  Change the default admin password after first login!${NC}"
echo ""
echo -e "   Useful commands:"
echo -e "     View logs:    ${CYAN}cd ${INSTALL_DIR} && ${COMPOSE_CMD} logs -f${NC}"
echo -e "     Stop:         ${CYAN}cd ${INSTALL_DIR} && ${COMPOSE_CMD} down${NC}"
echo -e "     Update:       ${CYAN}cd ${INSTALL_DIR} && git pull && ${COMPOSE_CMD} up -d --build${NC}"
echo ""
