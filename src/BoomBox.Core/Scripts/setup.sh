# Runs as root inside a freshly imported box, in place of the OS's first-run setup.
# BOX_HOST and BOX_PATH are prepended by BoxCreator.

set -u

[ -r /etc/os-release ] && . /etc/os-release
case "${ID:-}" in
    kali | ubuntu | debian | fedora | arch) default_user=$ID ;;
    *) default_user=user ;;
esac

# Account ---------------------------------------------------------------------
box_user=$(getent passwd 1000 | cut -d: -f1)
if [ -z "$box_user" ]; then
    box_user=$default_user
    useradd -m -u 1000 -U -s /bin/bash "$box_user" 2>/dev/null \
        || adduser -D -u 1000 -s /bin/sh "$box_user"
fi
for group in sudo wheel adm wireshark; do
    if getent group "$group" >/dev/null; then
        usermod -aG "$group" "$box_user" 2>/dev/null \
            || addgroup "$box_user" "$group" 2>/dev/null
    fi
done
[ "${ID:-}" = kali ] && [ -x /usr/bin/zsh ] && usermod -s /usr/bin/zsh "$box_user"

if [ -d /etc/sudoers.d ]; then
    echo "$box_user ALL=(ALL) NOPASSWD:ALL" > /etc/sudoers.d/boombox
    chmod 440 /etc/sudoers.d/boombox
fi

# Isolation from Windows ------------------------------------------------------
systemd=false
[ -x /lib/systemd/systemd ] || [ -x /usr/lib/systemd/systemd ] && systemd=true

cat > /etc/wsl.conf <<EOF
[boot]
systemd=$systemd

[automount]
enabled=false
mountFsTab=true

[interop]
enabled=false
appendWindowsPath=false

[network]
hostname=$BOX_HOST
generateResolvConf=true

[user]
default=$box_user
EOF

# Only the box folder crosses into Linux, at /box. fstab escapes spaces as \040.
fstab_path=$(printf '%s' "$BOX_PATH" | sed 's/ /\\040/g')
mkdir -p /box
printf '%s /box drvfs metadata,uid=1000,gid=1000,umask=022,fmask=011 0 0\n' "$fstab_path" > /etc/fstab

home=$(getent passwd "$box_user" | cut -d: -f6)
ln -sfn /box "$home/Box"
chown -h "$box_user" "$home/Box"

# Leftovers from booting off a DVD --------------------------------------------
rm -f /etc/resolv.conf
: > /etc/machine-id
rm -f /var/lib/dbus/machine-id
echo "LANG=C.UTF-8" | tee /etc/default/locale > /etc/locale.conf
echo "$BOX_HOST" > /etc/hostname
if ! grep -q "$BOX_HOST" /etc/hosts 2>/dev/null; then
    echo "127.0.1.1 $BOX_HOST" >> /etc/hosts
fi
[ -f /etc/apt/sources.list ] && sed -i '/^deb cdrom:/s/^/# /' /etc/apt/sources.list
grep -ls 'file:///cdrom' /etc/apt/sources.list.d/* 2>/dev/null | xargs -r rm -f

# Services that only make sense on real hardware or during a live boot.
if [ -d /etc/systemd/system ]; then
    mkdir -p /etc/cloud && touch /etc/cloud/cloud-init.disabled
    ln -sf /lib/systemd/system/multi-user.target /etc/systemd/system/default.target
    for unit in \
        cloud-init.service cloud-init-local.service cloud-init-main.service \
        cloud-init-network.service cloud-config.service cloud-final.service \
        multipathd.service multipathd.socket \
        snapd.service snapd.socket snapd.seeded.service \
        systemd-networkd-wait-online.service NetworkManager-wait-online.service \
        iscsid.service iscsid.socket open-iscsi.service \
        tpm-udev.path tpm-udev.service \
        lightdm.service gdm.service sddm.service casper.service \
        live-config.service
    do
        ln -sf /dev/null "/etc/systemd/system/$unit"
    done
fi

# Runs whatever the right-click menu hands to a box ---------------------------
cat > /usr/local/bin/boombox-open <<'EOF'
#!/bin/bash
file=$1
name=$(basename "$file")
stay() { echo; echo "[finished] Type exit to close this window."; exec "${SHELL:-/bin/bash}" -i; }

case "${name,,}" in
    *.deb)      sudo apt install -y "./$name" ;;
    *.rpm)      sudo dnf install -y "./$name" ;;
    *.appimage) chmod +x "$name"; "./$name" ;;
    *.sh)       bash "$name" ;;
    *.py)       python3 "$name" ;;
    *.exe|*.msi|*.bat|*.cmd)
                echo "$name is a Windows program and this box is Linux." ;;
    *)
        if [ -x "$name" ] && head -c 4 "$name" | grep -q ELF; then
            "./$name"
        elif command -v xdg-open >/dev/null && [ -n "${DISPLAY:-}" ]; then
            xdg-open "$name"
        else
            ${PAGER:-less} "$name"
        fi
        ;;
esac
stay
EOF
chmod 755 /usr/local/bin/boombox-open

touch /etc/boombox-release
echo "BB:USER $box_user"
