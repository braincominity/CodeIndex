#!/usr/bin/env bash
# install.sh — One-liner installer for cdidx (CodeIndex)
# cdidxワンライナーインストーラー
#
# Usage / 使い方:
#   curl -fsSL https://raw.githubusercontent.com/Widthdom/CodeIndex/main/install.sh | bash
#   curl -fsSL https://raw.githubusercontent.com/Widthdom/CodeIndex/v1.5.0/install.sh | bash -s -- v1.5.0
#   export CDIDX_INSTALL_DIR=/usr/local/bin; curl -fsSL ... | bash

set -euo pipefail

REPO="Widthdom/CodeIndex"
INSTALL_DIR="${CDIDX_INSTALL_DIR:-$HOME/.local/bin}"
BINARY_NAME="cdidx"
TMPDIR_CLEANUP=""
EXISTING_BIN=""
EXISTING_VERSION=""

# --- Helpers / ヘルパー ---

info()  { printf '\033[1;34m==>\033[0m %s\n' "$1"; }
warn()  { printf '\033[1;33mWARN:\033[0m %s\n' "$1" >&2; }
error() { printf '\033[1;31mERROR:\033[0m %s\n' "$1" >&2; exit 1; }

cleanup() {
    if [ -n "$TMPDIR_CLEANUP" ]; then
        rm -rf "$TMPDIR_CLEANUP"
    fi
}
trap cleanup EXIT

need_cmd() {
    if ! command -v "$1" > /dev/null 2>&1; then
        error "Required command not found: $1"
    fi
}

strip_version_prefix() {
    printf '%s' "$1" | sed 's/^[^0-9]*//'
}

extract_release_tag_name() {
    local api_response="$1"
    local version=""

    if command -v jq > /dev/null 2>&1; then
        version="$(printf '%s' "$api_response" | jq -r '.tag_name // empty' 2>/dev/null || true)"
    fi

    if [ -z "$version" ]; then
        version="$(printf '%s' "$api_response" | grep '"tag_name"' | head -1 | sed 's/.*"tag_name"[[:space:]]*:[[:space:]]*"\([^"]*\)".*/\1/')"
    fi

    printf '%s' "$version"
}

curl_http_get() {
    local url="$1"
    local output_path="$2"
    local http_code

    http_code="$(curl -sSL -o "$output_path" -w '%{http_code}' "$url")"
    local curl_status=$?

    if [ $curl_status -ne 0 ]; then
        case "$curl_status" in
            6|7|28|35|52|56)
                error "Network error reaching GitHub while fetching $url (curl exit $curl_status). Check your connection or corporate proxy."
                ;;
            *)
                error "curl failed while fetching $url (exit $curl_status)."
                ;;
        esac
    fi

    printf '%s' "$http_code"
}

fetch_latest_release_version() {
    need_cmd curl
    need_cmd mktemp

    local api_url="https://api.github.com/repos/${REPO}/releases/latest"
    local response_file
    response_file="$(mktemp)"

    local http_code
    http_code="$(curl_http_get "$api_url" "$response_file")"
    local api_response
    api_response="$(cat "$response_file")"
    rm -f "$response_file"

    case "$http_code" in
        200) ;;
        403)
            if printf '%s' "$api_response" | grep -qi "rate limit"; then
                error "GitHub API rate limit exceeded while fetching the latest release. Retry later, or pass an explicit version: 'curl ... | bash -s -- vX.Y.Z'."
            fi
            error "GitHub API returned HTTP 403 while fetching the latest release. Check your GitHub access or proxy configuration."
            ;;
        404)
            error "GitHub API returned HTTP 404 while fetching the latest release. Check that REPO=${REPO} exists."
            ;;
        5??)
            error "GitHub API returned HTTP $http_code while fetching the latest release. GitHub may be temporarily unavailable; retry in a few minutes."
            ;;
        *)
            error "GitHub API returned HTTP $http_code while fetching the latest release."
            ;;
    esac

    local version
    version="$(extract_release_tag_name "$api_response")"
    if [ -z "$version" ]; then
        error "Could not determine latest version from GitHub API response."
    fi

    printf '%s' "$version"
}

download_release_file() {
    local url="$1"
    local output_path="$2"
    local description="$3"

    local http_code
    http_code="$(curl_http_get "$url" "$output_path")"

    case "$http_code" in
        200) ;;
        403)
            error "Failed to download ${description} from $url (HTTP 403). GitHub may be rate-limiting or blocking the request."
            ;;
        404)
            error "Failed to download ${description} from $url (HTTP 404). Check that version ${VERSION} exists and publishes ${RID} assets."
            ;;
        5??)
            error "Failed to download ${description} from $url (HTTP $http_code). GitHub may be temporarily unavailable; retry in a few minutes."
            ;;
        *)
            error "Failed to download ${description} from $url (HTTP $http_code)."
            ;;
    esac
}

# --- Detect OS and architecture / OS・アーキテクチャ検出 ---

detect_platform() {
    local os arch
    os="$(uname -s)"
    arch="$(uname -m)"

    case "$os" in
        Linux)  OS_NAME="linux" ;;
        Darwin) OS_NAME="osx"   ;;
        *)      error "Unsupported OS: $os (supported: Linux, macOS)" ;;
    esac

    case "$arch" in
        x86_64|amd64)   ARCH_NAME="x64"   ;;
        aarch64|arm64)  ARCH_NAME="arm64"  ;;
        *)              error "Unsupported architecture: $arch (supported: x86_64, arm64)" ;;
    esac

    RID="${OS_NAME}-${ARCH_NAME}"

    # osx-x64 is not published / osx-x64 はリリースしていない
    if [ "$RID" = "osx-x64" ]; then
        error "macOS x86_64 (Intel) binaries are not published. Use Rosetta 2 with osx-arm64 or install via 'dotnet tool install -g cdidx'."
    fi

    # Reject musl-based Linux (e.g. Alpine) — published binaries require glibc
    # musl系Linux（Alpine等）を拒否 — リリースバイナリはglibcが必要
    if [ "$OS_NAME" = "linux" ]; then
        if command -v ldd > /dev/null 2>&1 && ldd --version 2>&1 | grep -qi musl; then
            error "musl-based Linux (e.g. Alpine) is not supported. Published binaries require glibc. Use a glibc-based image (e.g. debian, ubuntu) or install via 'dotnet tool install -g cdidx'."
        fi
    fi
}

# --- Resolve version / バージョン解決 ---

resolve_version() {
    if [ -n "${1:-}" ]; then
        VERSION="$1"
        # Ensure v prefix / vプレフィックスを補完
        case "$VERSION" in
            v*) ;;
            *)  VERSION="v${VERSION}" ;;
        esac
    else
        if [ -n "$EXISTING_VERSION" ]; then
            VERSION="v${EXISTING_VERSION}"
            info "cdidx ${EXISTING_VERSION} is already installed at ${EXISTING_BIN}. Skipping latest-release lookup. Pass an explicit version to reinstall or switch versions."
            return 1
        fi

        info "Fetching latest release version..."
        VERSION="$(fetch_latest_release_version)"
    fi

    info "Version: $VERSION"
    return 0
}

# --- Check existing installation / 既存インストール確認 ---

detect_existing_install() {
    EXISTING_BIN="${INSTALL_DIR}/${BINARY_NAME}"
    EXISTING_VERSION=""

    if [ -x "$EXISTING_BIN" ]; then
        local raw_version
        raw_version="$("$EXISTING_BIN" --version 2>/dev/null || echo "unknown")"
        # Strip any prefix like "cdidx " or "cdidx v" / プレフィックスを除去
        EXISTING_VERSION="$(strip_version_prefix "$raw_version")"
    fi

    return 0
}

check_existing() {
    if [ -n "$EXISTING_VERSION" ]; then
        local target_version="${VERSION#v}"
        if [ "$EXISTING_VERSION" = "$target_version" ]; then
            info "cdidx $target_version is already installed at $EXISTING_BIN. Skipping."
            exit 0
        fi
        info "Switching cdidx from $EXISTING_VERSION to ${VERSION#v}..."
    fi

    return 0
}

# --- Download and verify / ダウンロード・検証 ---

download_and_install() {
    need_cmd curl
    need_cmd tar
    need_cmd mktemp

    local archive_name="CodeIndex-${RID}.tar.gz"
    local base_url="https://github.com/${REPO}/releases/download/${VERSION}"
    local archive_url="${base_url}/${archive_name}"
    local checksums_url="${base_url}/sha256sums.txt"

    local tmpdir
    tmpdir="$(mktemp -d)"
    TMPDIR_CLEANUP="$tmpdir"

    info "Downloading ${archive_name}..."
    download_release_file "$archive_url" "${tmpdir}/${archive_name}" "${archive_name}"

    info "Downloading checksums..."
    download_release_file "$checksums_url" "${tmpdir}/sha256sums.txt" "sha256sums.txt"

    # Verify checksum / チェックサム検証
    info "Verifying checksum..."
    local expected_checksum
    expected_checksum="$(grep "$archive_name" "${tmpdir}/sha256sums.txt" | awk '{print $1}')"

    if [ -z "$expected_checksum" ]; then
        error "Checksum for $archive_name not found in sha256sums.txt."
    fi

    local actual_checksum
    if command -v sha256sum > /dev/null 2>&1; then
        actual_checksum="$(sha256sum "${tmpdir}/${archive_name}" | awk '{print $1}')"
    elif command -v shasum > /dev/null 2>&1; then
        actual_checksum="$(shasum -a 256 "${tmpdir}/${archive_name}" | awk '{print $1}')"
    elif command -v openssl > /dev/null 2>&1; then
        actual_checksum="$(openssl dgst -sha256 "${tmpdir}/${archive_name}" | awk '{print $NF}')"
    else
        error "No checksum tool found (need sha256sum, shasum, or openssl). Cannot verify download integrity."
    fi

    if [ "$actual_checksum" != "$expected_checksum" ]; then
        error "Checksum mismatch!\n  Expected: $expected_checksum\n  Actual:   $actual_checksum"
    fi

    # Extract into a dedicated subdirectory so we don't mix extracted files
    # with the downloaded archive/checksums when copying.
    # 展開用サブディレクトリを使い、アーカイブや checksum ファイルと混在させない。
    local extract_dir="${tmpdir}/extract"
    mkdir -p "$extract_dir"
    info "Extracting..."
    tar xzf "${tmpdir}/${archive_name}" -C "$extract_dir"

    # Install binary / バイナリをインストール
    mkdir -p "$INSTALL_DIR"
    cp "${extract_dir}/${BINARY_NAME}" "${INSTALL_DIR}/${BINARY_NAME}"
    chmod +x "${INSTALL_DIR}/${BINARY_NAME}"

    # Install runtime assets alongside the binary. Fail fast if any required
    # asset is missing rather than silently installing a partially broken
    # binary that will crash on first use.
    # - cdidx loads version.json via AppContext.BaseDirectory (the binary's dir),
    #   so without it `cdidx --version` reports v0.0.0.
    # - The native SQLite library (libe_sqlite3.so on Linux, libe_sqlite3.dylib
    #   on macOS) must live next to the binary for P/Invoke to resolve; without
    #   it every command crashes with DllNotFoundException at startup.
    # Required assets are OS-specific, so we match on $OS_NAME instead of
    # "copy whatever happens to be in the archive". This keeps the installer
    # compatible with bash 3.2 (the default /bin/bash on macOS) — no arrays,
    # no `mapfile`, no `find` — and works for all currently published tarballs.
    # ランタイム資産をバイナリの隣へ配置する。必須資産が欠落している場合は、
    # 部分的に壊れたインストールを黙って進めず即時失敗させる（起動直後の
    # クラッシュを防ぐため）。
    # - cdidx は AppContext.BaseDirectory（バイナリのディレクトリ）から
    #   version.json を読むため、これが無いと --version が v0.0.0 になる。
    # - ネイティブ SQLite ライブラリ（Linux は libe_sqlite3.so、macOS は
    #   libe_sqlite3.dylib）は P/Invoke 解決のためバイナリの隣に必要で、
    #   無いと起動直後に DllNotFoundException で全コマンドが落ちる。
    # 必須資産は OS ごとに異なるため「アーカイブにあるものを何でも」ではなく
    # $OS_NAME で分岐する。macOS の既定 /bin/bash 3.2 でも動くよう、配列・
    # `mapfile`・`find` は使わず、現行リリースの tarball 配置前提で実装する。
    local required_assets
    case "$OS_NAME" in
        linux) required_assets="version.json libe_sqlite3.so"   ;;
        osx)   required_assets="version.json libe_sqlite3.dylib" ;;
        *)     error "Internal error: unknown OS_NAME '$OS_NAME' for asset selection." ;;
    esac

    local asset
    for asset in $required_assets; do
        if [ ! -f "${extract_dir}/${asset}" ]; then
            error "Required runtime asset missing from release tarball: ${asset}. Refusing to install a partially broken binary. Please report this at https://github.com/${REPO}/issues."
        fi
        cp "${extract_dir}/${asset}" "${INSTALL_DIR}/${asset}"
    done

    info "Installed cdidx to ${INSTALL_DIR}/${BINARY_NAME}"
}

# --- PATH guidance / PATHガイダンス ---

check_path() {
    case ":${PATH}:" in
        *":${INSTALL_DIR}:"*) ;;
        *)
            warn "${INSTALL_DIR} is not in your PATH."
            echo ""
            echo "  Add it to your shell profile:"
            echo ""
            local shell_name
            shell_name="$(basename "${SHELL:-/bin/bash}")"
            case "$shell_name" in
                zsh)
                    echo "    echo 'export PATH=\"${INSTALL_DIR}:\$PATH\"' >> ~/.zshrc"
                    echo "    source ~/.zshrc"
                    ;;
                bash)
                    echo "    echo 'export PATH=\"${INSTALL_DIR}:\$PATH\"' >> ~/.bashrc"
                    echo "    source ~/.bashrc"
                    ;;
                fish)
                    echo "    fish_add_path ${INSTALL_DIR}"
                    ;;
                *)
                    echo "    export PATH=\"${INSTALL_DIR}:\$PATH\""
                    ;;
            esac
            echo ""
            ;;
    esac
}

# --- Main / メイン ---

main() {
    info "cdidx installer"
    detect_platform
    info "Detected platform: ${RID}"
    detect_existing_install
    if ! resolve_version "${1:-}"; then
        return 0
    fi
    check_existing
    download_and_install
    check_path

    echo ""
    info "Done! Run 'cdidx --version' to verify."
    echo ""
    echo "  Quick start:"
    echo "    cdidx .              # Index current directory"
    echo "    cdidx search <query> # Search your code"
    echo "    cdidx mcp            # Start MCP server for AI tools"
    echo ""
}

if [ "${CDIDX_INSTALL_SH_LIB_ONLY:-0}" != "1" ]; then
    main "$@"
fi
