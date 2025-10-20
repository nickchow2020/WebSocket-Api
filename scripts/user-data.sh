#!/bin/bash
# EC2 User Data Script for CloudFormation
# This script runs on instance first boot to setup the WebSocket API

set -e

# Logging
exec > >(tee /var/log/user-data.log)
exec 2>&1

echo "Starting WebSocket API setup at $(date)"

# Update system
apt-get update -y
apt-get upgrade -y

# Install dependencies
apt-get install -y git wget curl nginx

# Install .NET 8 Runtime
wget https://dot.net/v1/dotnet-install.sh -O /tmp/dotnet-install.sh
chmod +x /tmp/dotnet-install.sh
/tmp/dotnet-install.sh --channel 8.0 --install-dir /usr/share/dotnet
ln -sf /usr/share/dotnet/dotnet /usr/bin/dotnet

# Verify .NET installation
dotnet --version

# Clone application repository
cd /opt
git clone https://github.com/nickchow2020/WebSocket-Api.git websocketapi
cd websocketapi

# Build and publish application
dotnet publish WebSocketApi/WebSocketApi.csproj -c Release -o /var/www/websocketapi

# Set permissions
chown -R www-data:www-data /var/www/websocketapi

# Create systemd service
cat > /etc/systemd/system/websocketapi.service <<'EOF'
[Unit]
Description=WebSocket API .NET Application
After=network.target

[Service]
Type=notify
WorkingDirectory=/var/www/websocketapi
ExecStart=/usr/bin/dotnet /var/www/websocketapi/WebSocketApi.dll
Restart=always
RestartSec=10
KillSignal=SIGINT
SyslogIdentifier=websocketapi
User=www-data
Environment=ASPNETCORE_ENVIRONMENT=Production
Environment=ASPNETCORE_URLS=http://0.0.0.0:5000

[Install]
WantedBy=multi-user.target
EOF

# Enable and start service
systemctl daemon-reload
systemctl enable websocketapi
systemctl start websocketapi

# Configure Nginx
cat > /etc/nginx/sites-available/websocketapi <<'EOF'
upstream websocketapi {
    server 127.0.0.1:5000;
}

server {
    listen 80 default_server;
    server_name _;

    location / {
        proxy_pass http://websocketapi;
        proxy_http_version 1.1;
        proxy_set_header Upgrade $http_upgrade;
        proxy_set_header Connection "upgrade";
        proxy_set_header Host $host;
        proxy_set_header X-Real-IP $remote_addr;
        proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
        proxy_set_header X-Forwarded-Proto $scheme;
        proxy_cache_bypass $http_upgrade;
        proxy_read_timeout 300s;
        proxy_connect_timeout 75s;
    }

    location /ws {
        proxy_pass http://websocketapi/ws;
        proxy_http_version 1.1;
        proxy_set_header Upgrade $http_upgrade;
        proxy_set_header Connection "upgrade";
        proxy_set_header Host $host;
        proxy_set_header X-Real-IP $remote_addr;
        proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
        proxy_set_header X-Forwarded-Proto $scheme;
        proxy_read_timeout 86400;
    }

    location /health {
        proxy_pass http://websocketapi/health;
        access_log off;
    }
}
EOF

ln -sf /etc/nginx/sites-available/websocketapi /etc/nginx/sites-enabled/
rm -f /etc/nginx/sites-enabled/default

nginx -t
systemctl restart nginx

echo "WebSocket API setup completed at $(date)"
