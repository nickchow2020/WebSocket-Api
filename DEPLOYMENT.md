# EC2 Deployment Guide

This guide covers deploying the WebSocket API to AWS EC2.

## Prerequisites

- AWS Account with EC2 access
- EC2 instance running (Ubuntu 22.04 LTS recommended)
- Domain name pointed to EC2 instance (for SSL/WSS)
- SSH key pair for EC2 access

## Step 1: Launch EC2 Instance

**Recommended Configuration:**
- Instance Type: t3.small or t3.medium (depending on traffic)
- OS: Ubuntu 22.04 LTS
- Storage: 20GB gp3
- Security Group Rules:
  - SSH (22) - Your IP only
  - HTTP (80) - 0.0.0.0/0
  - HTTPS (443) - 0.0.0.0/0
  - Custom TCP (5000) - 0.0.0.0/0 (temporary, will use reverse proxy)

## Step 2: Install .NET 8 Runtime on EC2

SSH into your EC2 instance:
```bash
ssh -i your-key.pem ubuntu@your-ec2-ip
```

Install .NET 8 Runtime:
```bash
# Update packages
sudo apt update && sudo apt upgrade -y

# Install .NET 8 SDK
wget https://dot.net/v1/dotnet-install.sh -O dotnet-install.sh
chmod +x dotnet-install.sh
./dotnet-install.sh --channel 8.0

# Add to PATH
echo 'export DOTNET_ROOT=$HOME/.dotnet' >> ~/.bashrc
echo 'export PATH=$PATH:$DOTNET_ROOT:$DOTNET_ROOT/tools' >> ~/.bashrc
source ~/.bashrc

# Verify installation
dotnet --version
```

## Step 3: Clone and Build Your Application

```bash
# Install git
sudo apt install git -y

# Clone your repository
cd ~
git clone https://github.com/nickchow2020/WebSocket-Api.git
cd WebSocket-Api

# Build the application
dotnet build WebSocketApi/WebSocketApi.csproj -c Release

# Publish the application
dotnet publish WebSocketApi/WebSocketApi.csproj -c Release -o /var/www/websocketapi
```

## Step 4: Configure Environment Variables

Create environment configuration:
```bash
sudo nano /etc/environment
```

Add these variables:
```
ASPNETCORE_ENVIRONMENT=Production
ASPNETCORE_URLS=http://0.0.0.0:5000
```

Apply changes:
```bash
source /etc/environment
```

## Step 5: Create Systemd Service

Create a service file to run your app automatically:
```bash
sudo nano /etc/systemd/system/websocketapi.service
```

Add this content:
```ini
[Unit]
Description=WebSocket API .NET Application
After=network.target

[Service]
Type=notify
WorkingDirectory=/var/www/websocketapi
ExecStart=/home/ubuntu/.dotnet/dotnet /var/www/websocketapi/WebSocketApi.dll
Restart=always
RestartSec=10
KillSignal=SIGINT
SyslogIdentifier=websocketapi
User=www-data
Environment=ASPNETCORE_ENVIRONMENT=Production
Environment=ASPNETCORE_URLS=http://0.0.0.0:5000
Environment=DOTNET_ROOT=/home/ubuntu/.dotnet

[Install]
WantedBy=multi-user.target
```

Enable and start the service:
```bash
# Set permissions
sudo chown -R www-data:www-data /var/www/websocketapi

# Reload systemd
sudo systemctl daemon-reload

# Enable service to start on boot
sudo systemctl enable websocketapi

# Start the service
sudo systemctl start websocketapi

# Check status
sudo systemctl status websocketapi

# View logs
sudo journalctl -u websocketapi -f
```

## Step 6: Install and Configure Nginx (Reverse Proxy)

Install Nginx:
```bash
sudo apt install nginx -y
```

Create Nginx configuration:
```bash
sudo nano /etc/nginx/sites-available/websocketapi
```

Add this configuration:
```nginx
upstream websocketapi {
    server 127.0.0.1:5000;
}

server {
    listen 80;
    server_name your-domain.com www.your-domain.com;

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

    # WebSocket specific endpoint
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

    # Health check endpoint
    location /health {
        proxy_pass http://websocketapi/health;
        access_log off;
    }
}
```

Enable the site:
```bash
# Create symlink
sudo ln -s /etc/nginx/sites-available/websocketapi /etc/nginx/sites-enabled/

# Remove default site
sudo rm /etc/nginx/sites-enabled/default

# Test configuration
sudo nginx -t

# Restart Nginx
sudo systemctl restart nginx
```

## Step 7: Install SSL Certificate (Let's Encrypt)

Install Certbot:
```bash
sudo apt install certbot python3-certbot-nginx -y
```

Obtain SSL certificate:
```bash
sudo certbot --nginx -d your-domain.com -d www.your-domain.com
```

Follow prompts to:
- Enter email address
- Agree to terms
- Choose to redirect HTTP to HTTPS (recommended)

Auto-renewal is configured automatically. Test it:
```bash
sudo certbot renew --dry-run
```

## Step 8: Update Production Configuration

Update CORS origins in production:
```bash
sudo nano /var/www/websocketapi/appsettings.Production.json
```

Update `AllowedOrigins` to your actual frontend domain:
```json
{
  "CorsSettings": {
    "AllowedOrigins": [
      "https://your-actual-frontend-domain.com"
    ]
  }
}
```

Restart the service:
```bash
sudo systemctl restart websocketapi
```

## Step 9: Configure Firewall (UFW)

```bash
# Enable UFW
sudo ufw enable

# Allow SSH
sudo ufw allow 22/tcp

# Allow HTTP/HTTPS
sudo ufw allow 80/tcp
sudo ufw allow 443/tcp

# Check status
sudo ufw status
```

## Step 10: Verify Deployment

Test your endpoints:

**Health Check:**
```bash
curl http://your-domain.com/health
# Should return: Healthy
```

**WebSocket Connection:**
```bash
# From your Next.js frontend, update WebSocket URL to:
wss://your-domain.com/ws
```

## Useful Commands

**View Application Logs:**
```bash
sudo journalctl -u websocketapi -f
```

**Restart Application:**
```bash
sudo systemctl restart websocketapi
```

**Check Nginx Logs:**
```bash
sudo tail -f /var/log/nginx/error.log
sudo tail -f /var/log/nginx/access.log
```

**Update Application:**
```bash
cd ~/WebSocket-Api
git pull
dotnet publish WebSocketApi/WebSocketApi.csproj -c Release -o /var/www/websocketapi
sudo systemctl restart websocketapi
```

## Monitoring & Maintenance

**Check System Resources:**
```bash
htop
df -h
free -m
```

**Check Application Status:**
```bash
sudo systemctl status websocketapi
sudo systemctl status nginx
```

**View Active Connections:**
```bash
sudo netstat -tulpn | grep :5000
```

## Troubleshooting

**Application won't start:**
```bash
# Check logs
sudo journalctl -u websocketapi -n 50

# Check permissions
ls -la /var/www/websocketapi

# Verify .NET installation
dotnet --info
```

**WebSocket connection fails:**
```bash
# Check Nginx configuration
sudo nginx -t

# Verify proxy settings
sudo cat /etc/nginx/sites-available/websocketapi

# Check firewall
sudo ufw status
```

**SSL issues:**
```bash
# Renew certificate
sudo certbot renew

# Check certificate status
sudo certbot certificates
```

## Security Best Practices

1. **Keep system updated:**
   ```bash
   sudo apt update && sudo apt upgrade -y
   ```

2. **Use environment variables for secrets** (never commit to git)

3. **Restrict SSH access** to specific IPs in Security Group

4. **Enable automatic security updates:**
   ```bash
   sudo apt install unattended-upgrades
   sudo dpkg-reconfigure -plow unattended-upgrades
   ```

5. **Set up CloudWatch** for monitoring (optional but recommended)

6. **Regular backups** of configuration files

## Cost Optimization

- Use **Elastic IP** to avoid IP changes
- Consider **Application Load Balancer** for multiple instances
- Set up **Auto Scaling** for high traffic
- Use **CloudWatch Alarms** for resource monitoring
- Consider **Reserved Instances** for long-term cost savings

## Next Steps After Deployment

1. Update your Next.js frontend WebSocket URL to `wss://your-domain.com/ws`
2. Update CORS origins in `appsettings.Production.json`
3. Monitor logs for the first few hours
4. Set up CloudWatch monitoring (optional)
5. Configure automated backups
6. Document your domain and SSL certificate renewal dates
