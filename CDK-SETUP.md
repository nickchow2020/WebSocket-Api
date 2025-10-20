# CDK + GitHub Actions Deployment Setup

This guide helps you deploy the WebSocket API to EC2 using AWS CDK from your infrastructure repo, with automated deployments via GitHub Actions.

## Overview

**Architecture:**
```
GitHub → GitHub Actions → Build .NET App → Upload to S3 → Deploy to EC2 via SSM
```

Your CDK infrastructure repo creates the EC2 instance, and this repo (WebSocket API) handles the application deployment.

## Prerequisites

### 1. AWS Resources (Created by Your CDK Infrastructure Repo)

Your CDK stack should create:

- ✅ **EC2 Instance** running Ubuntu 22.04
- ✅ **S3 Bucket** for deployment packages
- ✅ **IAM Role** for EC2 with:
  - `AmazonSSMManagedInstanceCore` (for SSM commands)
  - S3 read access to deployment bucket
- ✅ **Security Group** allowing:
  - Port 80/443 (from ALB or 0.0.0.0/0)
  - Port 22 (optional, for troubleshooting)
- ✅ **ALB** (Application Load Balancer) pointing to EC2
- ✅ **EC2 Instance Tag**: `Name: WebSocketApi` (or your custom name)

### 2. EC2 Initial Setup (One-time via User Data or Manual)

The EC2 instance needs these prerequisites installed:

```bash
# Install .NET 8
wget https://dot.net/v1/dotnet-install.sh -O /tmp/dotnet-install.sh
chmod +x /tmp/dotnet-install.sh
/tmp/dotnet-install.sh --channel 8.0 --install-dir /usr/share/dotnet
ln -sf /usr/share/dotnet/dotnet /usr/bin/dotnet

# Install AWS CLI (if not pre-installed)
sudo apt-get update
sudo apt-get install -y awscli unzip

# Create app directory
sudo mkdir -p /var/www/websocketapi
sudo chown -R ubuntu:ubuntu /var/www/websocketapi

# Create systemd service
sudo tee /etc/systemd/system/websocketapi.service > /dev/null <<'EOF'
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

sudo systemctl daemon-reload
sudo systemctl enable websocketapi

# Install Nginx (optional, if ALB isn't used)
sudo apt-get install -y nginx
# Configure nginx as reverse proxy (see DEPLOYMENT.md)
```

**Add this to your CDK User Data** or run manually once on the EC2 instance.

## GitHub Actions Setup

### Step 1: Create GitHub Secrets

Go to your GitHub repo: **Settings → Secrets and variables → Actions → New repository secret**

Add these secrets:

| Secret Name | Value | Description |
|------------|-------|-------------|
| `AWS_ACCESS_KEY_ID` | Your AWS Access Key | IAM user with EC2 and S3 permissions |
| `AWS_SECRET_ACCESS_KEY` | Your AWS Secret Key | Corresponding secret key |
| `S3_DEPLOYMENT_BUCKET` | `your-deployment-bucket-name` | S3 bucket created by CDK |
| `EC2_INSTANCE_TAG` | `WebSocketApi` | EC2 instance Name tag |

### Step 2: Create IAM User for GitHub Actions

Create an IAM user with these permissions:

```json
{
  "Version": "2012-10-17",
  "Statement": [
    {
      "Effect": "Allow",
      "Action": [
        "s3:PutObject",
        "s3:GetObject",
        "s3:ListBucket"
      ],
      "Resource": [
        "arn:aws:s3:::your-deployment-bucket-name",
        "arn:aws:s3:::your-deployment-bucket-name/*"
      ]
    },
    {
      "Effect": "Allow",
      "Action": [
        "ssm:SendCommand",
        "ssm:GetCommandInvocation",
        "ec2:DescribeInstances"
      ],
      "Resource": "*"
    }
  ]
}
```

### Step 3: Verify EC2 IAM Role

Your EC2 instance role needs:

```json
{
  "Version": "2012-10-17",
  "Statement": [
    {
      "Effect": "Allow",
      "Action": [
        "s3:GetObject",
        "s3:ListBucket"
      ],
      "Resource": [
        "arn:aws:s3:::your-deployment-bucket-name",
        "arn:aws:s3:::your-deployment-bucket-name/*"
      ]
    }
  ]
}
```

Plus the managed policy: `AmazonSSMManagedInstanceCore`

## CDK Infrastructure Code (Reference)

Here's what your CDK infrastructure repo should create (TypeScript example):

```typescript
import * as cdk from 'aws-cdk-lib';
import * as ec2 from 'aws-cdk-lib/aws-ec2';
import * as iam from 'aws-cdk-lib/aws-iam';
import * as s3 from 'aws-cdk-lib/aws-s3';
import * as elbv2 from 'aws-cdk-lib/aws-elasticloadbalancingv2';

export class WebSocketApiStack extends cdk.Stack {
  constructor(scope: cdk.App, id: string, props?: cdk.StackProps) {
    super(scope, id, props);

    // S3 bucket for deployments
    const deploymentBucket = new s3.Bucket(this, 'DeploymentBucket', {
      bucketName: 'your-websocket-deployments',
      removalPolicy: cdk.RemovalPolicy.RETAIN,
      encryption: s3.BucketEncryption.S3_MANAGED,
    });

    // VPC
    const vpc = new ec2.Vpc(this, 'VPC', {
      maxAzs: 2,
      natGateways: 1,
    });

    // Security Group
    const securityGroup = new ec2.SecurityGroup(this, 'WebSocketApiSG', {
      vpc,
      description: 'Security group for WebSocket API EC2',
      allowAllOutbound: true,
    });

    securityGroup.addIngressRule(
      ec2.Peer.anyIpv4(),
      ec2.Port.tcp(80),
      'Allow HTTP traffic'
    );

    securityGroup.addIngressRule(
      ec2.Peer.anyIpv4(),
      ec2.Port.tcp(443),
      'Allow HTTPS traffic'
    );

    // IAM Role for EC2
    const role = new iam.Role(this, 'WebSocketApiRole', {
      assumedBy: new iam.ServicePrincipal('ec2.amazonaws.com'),
      managedPolicies: [
        iam.ManagedPolicy.fromAwsManagedPolicyName('AmazonSSMManagedInstanceCore'),
      ],
    });

    deploymentBucket.grantRead(role);

    // User Data
    const userData = ec2.UserData.forLinux();
    userData.addCommands(
      '#!/bin/bash',
      'set -e',
      'apt-get update',
      'apt-get install -y awscli unzip nginx',

      // Install .NET 8
      'wget https://dot.net/v1/dotnet-install.sh -O /tmp/dotnet-install.sh',
      'chmod +x /tmp/dotnet-install.sh',
      '/tmp/dotnet-install.sh --channel 8.0 --install-dir /usr/share/dotnet',
      'ln -sf /usr/share/dotnet/dotnet /usr/bin/dotnet',

      // Create app directory
      'mkdir -p /var/www/websocketapi',
      'chown -R www-data:www-data /var/www/websocketapi',

      // Create systemd service
      'cat > /etc/systemd/system/websocketapi.service <<EOF',
      '[Unit]',
      'Description=WebSocket API',
      'After=network.target',
      '',
      '[Service]',
      'Type=notify',
      'WorkingDirectory=/var/www/websocketapi',
      'ExecStart=/usr/bin/dotnet /var/www/websocketapi/WebSocketApi.dll',
      'Restart=always',
      'User=www-data',
      'Environment=ASPNETCORE_ENVIRONMENT=Production',
      'Environment=ASPNETCORE_URLS=http://0.0.0.0:5000',
      '',
      '[Install]',
      'WantedBy=multi-user.target',
      'EOF',

      'systemctl daemon-reload',
      'systemctl enable websocketapi',

      // Configure Nginx
      'cat > /etc/nginx/sites-available/websocketapi <<EOF',
      'server {',
      '    listen 80;',
      '    location / {',
      '        proxy_pass http://localhost:5000;',
      '        proxy_http_version 1.1;',
      '        proxy_set_header Upgrade \\$http_upgrade;',
      '        proxy_set_header Connection "upgrade";',
      '        proxy_set_header Host \\$host;',
      '    }',
      '    location /ws {',
      '        proxy_pass http://localhost:5000/ws;',
      '        proxy_http_version 1.1;',
      '        proxy_set_header Upgrade \\$http_upgrade;',
      '        proxy_set_header Connection "upgrade";',
      '        proxy_read_timeout 86400;',
      '    }',
      '}',
      'EOF',

      'ln -sf /etc/nginx/sites-available/websocketapi /etc/nginx/sites-enabled/',
      'rm -f /etc/nginx/sites-enabled/default',
      'nginx -t && systemctl restart nginx'
    );

    // EC2 Instance
    const instance = new ec2.Instance(this, 'WebSocketApiInstance', {
      vpc,
      vpcSubnets: { subnetType: ec2.SubnetType.PUBLIC },
      instanceType: ec2.InstanceType.of(ec2.InstanceClass.T3, ec2.InstanceSize.SMALL),
      machineImage: ec2.MachineImage.fromSsmParameter(
        '/aws/service/canonical/ubuntu/server/22.04/stable/current/amd64/hvm/ebs-gp2/ami-id'
      ),
      securityGroup,
      role,
      userData,
    });

    cdk.Tags.of(instance).add('Name', 'WebSocketApi');

    // ALB
    const alb = new elbv2.ApplicationLoadBalancer(this, 'ALB', {
      vpc,
      internetFacing: true,
    });

    const targetGroup = new elbv2.ApplicationTargetGroup(this, 'TargetGroup', {
      vpc,
      port: 80,
      protocol: elbv2.ApplicationProtocol.HTTP,
      targets: [new ec2.InstanceTarget(instance, 80)],
      healthCheck: {
        path: '/health',
        interval: cdk.Duration.seconds(30),
      },
      stickinessCookieDuration: cdk.Duration.hours(1),
    });

    alb.addListener('HttpListener', {
      port: 80,
      defaultTargetGroups: [targetGroup],
    });

    // Outputs
    new cdk.CfnOutput(this, 'ALBDnsName', {
      value: alb.loadBalancerDnsName,
      description: 'ALB DNS Name',
    });

    new cdk.CfnOutput(this, 'DeploymentBucket', {
      value: deploymentBucket.bucketName,
      description: 'S3 Deployment Bucket',
    });

    new cdk.CfnOutput(this, 'InstanceId', {
      value: instance.instanceId,
      description: 'EC2 Instance ID',
    });
  }
}
```

## Deployment Workflow

### Automatic Deployment

Every push to `main` branch triggers:

1. ✅ Build .NET application
2. ✅ Run tests (if any)
3. ✅ Publish application
4. ✅ Upload to S3
5. ✅ Deploy to EC2 via SSM
6. ✅ Restart application
7. ✅ Verify health check

### Manual Deployment

Go to **Actions** tab → **Deploy WebSocket API to EC2** → **Run workflow**

### Monitor Deployment

- **GitHub Actions**: See logs in the Actions tab
- **EC2 Logs**:
  ```bash
  sudo journalctl -u websocketapi -f
  ```
- **SSM Logs**: Check AWS Systems Manager → Run Command

## Troubleshooting

### Deployment fails with "No instances found"

**Issue:** EC2 instance tag doesn't match
**Fix:** Update `EC2_INSTANCE_TAG` secret to match your instance's Name tag

### SSM command fails

**Issue:** EC2 doesn't have SSM agent or IAM role
**Fix:**
- Ensure IAM role has `AmazonSSMManagedInstanceCore`
- Restart SSM agent: `sudo systemctl restart amazon-ssm-agent`

### Application won't start

**Issue:** Missing dependencies or configuration
**Fix:**
```bash
# SSH into EC2
sudo journalctl -u websocketapi -n 50
# Check for errors
```

### Health check fails

**Issue:** Application not listening on correct port
**Fix:**
```bash
# Verify app is running
sudo systemctl status websocketapi

# Test locally
curl http://localhost:5000/health
```

## Testing the Deployment

After deployment completes:

```bash
# Get ALB DNS from CDK outputs
ALB_DNS="your-alb-xxxxx.us-east-1.elb.amazonaws.com"

# Test health endpoint
curl http://$ALB_DNS/health

# Test WebSocket (from browser console)
const ws = new WebSocket('ws://your-alb-xxxxx.us-east-1.elb.amazonaws.com/ws');
ws.onopen = () => ws.send('start_stream');
ws.onmessage = (e) => console.log(JSON.parse(e.data));
```

## Updating CORS Settings

After deployment, update your frontend domain:

```bash
# SSH into EC2
ssh -i your-key.pem ubuntu@your-ec2-ip

# Edit appsettings
sudo nano /var/www/websocketapi/appsettings.Production.json

# Update AllowedOrigins with your frontend domain
{
  "CorsSettings": {
    "AllowedOrigins": [
      "https://your-frontend-domain.com"
    ]
  }
}

# Restart service
sudo systemctl restart websocketapi
```

## Next Steps

1. ✅ Create CDK infrastructure stack in your infrastructure repo
2. ✅ Deploy CDK stack: `cdk deploy`
3. ✅ Note the outputs (ALB DNS, S3 bucket name, Instance tag)
4. ✅ Add GitHub secrets (AWS credentials, bucket name, instance tag)
5. ✅ Push to main branch → Triggers automatic deployment
6. ✅ Update CORS settings with your frontend domain
7. ✅ Test WebSocket connection from your Next.js app

## Cost Estimate

**Monthly costs for single instance:**
- EC2 t3.small: ~$15
- ALB: ~$20
- S3 (negligible): ~$1
- Data transfer: ~$5-10
- **Total: ~$40-50/month**
