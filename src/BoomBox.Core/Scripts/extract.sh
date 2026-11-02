# Runs as root inside the helper distro. DRIVE is prepended by BoxCreator.
# stdout carries the root filesystem as a tar stream.
# stderr carries progress lines: BB:STAGE, BB:OS, BB:SIZE, BB:ERROR.

W=$(mktemp -d /tmp/bb.XXXXXX)
ISO="$W/iso"
ROOT=""

say() { echo "BB:$*" >&2; }
die() { say "ERROR $*"; exit 1; }

cleanup() {
    grep -F " $W/" /proc/mounts | cut -d' ' -f2 | sort -r | while read -r m; do
        umount -l "$m" 2>/dev/null
    done
    rm -rf "$W" 2>/dev/null
}

# Prints the image files of the OS, bottom layer first.
find_layers() {
    if [ -f casper/install-sources.yaml ]; then
        # Ubuntu layers are named a.squashfs, a.b.squashfs, a.b.c.squashfs.
        src=$(awk '
            /^- /            { if (d && p) print p; if (!first && p) first = p; d = 0; p = "" }
            /default: true/  { d = 1 }
            /^ *path:/       { p = $2 }
            END              { if (d && p) print p; else print "FALLBACK " (first ? first : p) }
        ' casper/install-sources.yaml | head -n 1)
        src=${src#FALLBACK }
        base=${src%.squashfs}
        built=""
        for part in $(echo "$base" | tr '.' ' '); do
            built="${built:+$built.}$part"
            [ -f "casper/$built.squashfs" ] && echo "casper/$built.squashfs"
        done
        return
    fi
    for f in live/filesystem.squashfs casper/filesystem.squashfs \
             LiveOS/squashfs.img arch/x86_64/airootfs.sfs; do
        if [ -f "$f" ]; then echo "$f"; return; fi
    done
    # Remixes put the image somewhere else; the biggest one is the OS.
    find . -maxdepth 3 -type f \( -name '*.squashfs' -o -name '*.sfs' \) \
        -exec ls -s {} + 2>/dev/null | sort -n | tail -n 1 | awk '{print $2}' | sed 's|^\./||'
}

mount_rootfs() {
    n=0
    lower=""
    for layer in $LAYERS; do
        n=$((n + 1))
        mkdir -p "$W/l$n"
        mount -o loop,ro -t squashfs "$ISO/$layer" "$W/l$n" 2>/dev/null \
            || die "Could not open $layer inside the ISO. The file may be damaged."
        lower="$W/l$n${lower:+:$lower}"
    done

    if [ "$n" -eq 1 ]; then
        ROOT="$W/l1"
    else
        mkdir -p "$W/merged"
        mount -t overlay overlay -o "ro,lowerdir=$lower" "$W/merged" \
            || die "Could not merge the OS layers in this ISO."
        ROOT="$W/merged"
    fi

    # Fedora style: the squashfs only wraps a real ext4 image.
    if [ -f "$ROOT/LiveOS/rootfs.img" ]; then
        mkdir -p "$W/ext"
        mount -o loop,ro "$ROOT/LiveOS/rootfs.img" "$W/ext" \
            || die "Could not open the system image inside this ISO."
        ROOT="$W/ext"
    fi
}

main() {
    mkdir -p "$ISO"
    say "STAGE Reading the ISO"
    mount -t drvfs "$DRIVE:" "$ISO" 2>/dev/null || die "The helper could not read drive $DRIVE:."
    cd "$ISO" || die "The helper could not open drive $DRIVE:."

    LAYERS=$(find_layers)
    if [ -z "$LAYERS" ]; then
        die "This is an installer-only ISO, so it has no ready-made system to extract. Download the Live version of this OS instead. For Kali that is kali-linux-...-live-amd64.iso, which Kali offers as a torrent only."
    fi
    cd / || exit 1

    say "STAGE Opening the operating system"
    mount_rootfs
    if [ ! -f "$ROOT/etc/os-release" ] || [ ! -d "$ROOT/usr/bin" ]; then
        die "No Linux system was found inside this ISO. Only Linux Live ISOs work with BoomBox."
    fi

    machine=$(od -An -tx1 -j18 -N1 "$ROOT/usr/bin/env" 2>/dev/null | tr -d ' ')
    if [ -n "$machine" ] && [ "$machine" != "3e" ]; then
        die "This ISO is not built for 64-bit Intel/AMD PCs, so it cannot run on this computer."
    fi

    name=$(sed -n 's/^PRETTY_NAME=//p' "$ROOT/etc/os-release" | tr -d '"')
    say "OS ${name:-Linux}"

    say "STAGE Measuring the system"
    kb=$(du -sk "$ROOT" | cut -f1)
    say "SIZE $((kb * 1024))"

    say "STAGE Copying the operating system into your folder"
    # The OS's own GNU tar keeps file capabilities that busybox tar would drop.
    if [ -x "$ROOT/usr/bin/tar" ]; then
        chroot "$ROOT" /usr/bin/tar --numeric-owner --xattrs --xattrs-include='*' \
            --exclude='./proc/*' --exclude='./sys/*' --exclude='./dev/*' \
            --exclude='./run/*' --exclude='./tmp/*' --exclude='./swap.img' \
            -cpf - -C / . || die "Copying the system failed."
    else
        tar -C "$ROOT" --exclude='./proc/*' --exclude='./sys/*' --exclude='./dev/*' \
            --exclude='./run/*' --exclude='./tmp/*' --exclude='./swap.img' \
            -cf - . || die "Copying the system failed."
    fi
}

trap cleanup EXIT
trap 'exit 1' INT TERM
main </dev/null
