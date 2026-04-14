#!/usr/bin/env bash
set -euo pipefail

# Setup script for Ubuntu 22.04 LTS
# Installs all runtimes and tools needed to build and run payflow.
# Can be used as an Instruqt lifecycle script.

export DEBIAN_FRONTEND=noninteractive

apt-get update -y
apt-get install -y curl wget git make jq unzip apt-transport-https ca-certificates gnupg lsb-release

# --- Docker ---
install -m 0755 -d /etc/apt/keyrings
curl -fsSL https://download.docker.com/linux/ubuntu/gpg | gpg --dearmor -o /etc/apt/keyrings/docker.gpg
chmod a+r /etc/apt/keyrings/docker.gpg
echo "deb [arch=$(dpkg --print-architecture) signed-by=/etc/apt/keyrings/docker.gpg] https://download.docker.com/linux/ubuntu $(lsb_release -cs) stable" \
  > /etc/apt/sources.list.d/docker.list
apt-get update -y
apt-get install -y docker-ce docker-ce-cli containerd.io docker-buildx-plugin docker-compose-plugin
systemctl enable docker
systemctl start docker

# --- kubectl ---
curl -fsSL https://pkgs.k8s.io/core:/stable:/v1.30/deb/Release.key | gpg --dearmor -o /etc/apt/keyrings/kubernetes-apt-keyring.gpg
echo "deb [signed-by=/etc/apt/keyrings/kubernetes-apt-keyring.gpg] https://pkgs.k8s.io/core:/stable:/v1.30/deb/ /" \
  > /etc/apt/sources.list.d/kubernetes.list
apt-get update -y
apt-get install -y kubectl

# --- OpenJDK 25 ---
apt-get install -y openjdk-25-jdk || {
  # Fallback: install via sdkman if package not available
  curl -s "https://get.sdkman.io" | bash
  source "$HOME/.sdkman/bin/sdkman-init.sh"
  sdk install java 25-open
}

# --- Python 3.14.4 ---
apt-get install -y python3.14 python3.14-venv python3-pip || {
  add-apt-repository -y ppa:deadsnakes/ppa
  apt-get update -y
  apt-get install -y python3.14 python3.14-venv python3.14-distutils
  curl -sS https://bootstrap.pypa.io/get-pip.py | python3.14
}
update-alternatives --install /usr/bin/python3 python3 /usr/bin/python3.14 1

# --- Node.js 24.14.1 LTS ---
curl -fsSL https://deb.nodesource.com/setup_24.x | bash -
apt-get install -y nodejs

# --- .NET 10.0.5 ---
wget https://dot.net/v1/dotnet-install.sh -O /tmp/dotnet-install.sh
chmod +x /tmp/dotnet-install.sh
/tmp/dotnet-install.sh --version 10.0.5 --install-dir /usr/local/dotnet
echo 'export DOTNET_ROOT=/usr/local/dotnet' >> /etc/environment
echo 'export PATH=$PATH:/usr/local/dotnet' >> /etc/environment
ln -sf /usr/local/dotnet/dotnet /usr/local/bin/dotnet

# --- Go 1.26.2 ---
GO_VERSION=1.26.2
wget -q "https://go.dev/dl/go${GO_VERSION}.linux-amd64.tar.gz" -O /tmp/go.tar.gz
rm -rf /usr/local/go
tar -C /usr/local -xzf /tmp/go.tar.gz
echo 'export PATH=$PATH:/usr/local/go/bin' >> /etc/environment
ln -sf /usr/local/go/bin/go /usr/local/bin/go

# --- Ruby 3.2.11 ---
apt-get install -y ruby3.2 ruby3.2-dev || {
  apt-get install -y rbenv || {
    curl -fsSL https://rbenv.org/install.sh | bash
  }
  rbenv install 3.2.11
  rbenv global 3.2.11
}
gem install bundler --no-document

echo ""
echo "Setup complete. Runtime versions:"
java -version 2>&1 | head -1
python3 --version
node --version
dotnet --version
go version
ruby --version
docker --version
docker compose version
kubectl version --client
