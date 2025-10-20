# GitHub Actions Setup - Quick Start

Simple guide to set up automated deployments to your EC2 instance.

## Step 1: Add GitHub Secrets

Go to your repo: **Settings → Secrets and variables → Actions → New repository secret**

Add these 4 secrets:

| Secret Name | Where to Find It |
|------------|------------------|
| `AWS_ACCESS_KEY_ID` | Create IAM user → Security credentials → Create access key |
| `AWS_SECRET_ACCESS_KEY` | Same as above (shown once, copy it!) |
| `S3_DEPLOYMENT_BUCKET` | Your CDK outputs or S3 console (bucket name only, no `s3://`) |
| `EC2_INSTANCE_TAG` | EC2 console → Your instance → Tags → Name value (e.g., `WebSocketApi`) |

## Step 2: IAM User Permissions

Create an IAM user for GitHub Actions with this policy:

```json
{
  "Version": "2012-10-17",
  "Statement": [
    {
      "Effect": "Allow",
      "Action": [
        "s3:PutObject",
        "s3:GetObject"
      ],
      "Resource": "arn:aws:s3:::YOUR-BUCKET-NAME/*"
    },
    {
      "Effect": "Allow",
      "Action": [
        "ssm:SendCommand",
        "ec2:DescribeInstances"
      ],
      "Resource": "*"
    }
  ]
}
```

## Step 3: EC2 IAM Role

Your EC2 instance needs this role attached (should be in your CDK):

**Managed Policy:**
- `AmazonSSMManagedInstanceCore`

**Inline Policy:**
```json
{
  "Version": "2012-10-17",
  "Statement": [
    {
      "Effect": "Allow",
      "Action": ["s3:GetObject"],
      "Resource": "arn:aws:s3:::YOUR-BUCKET-NAME/*"
    }
  ]
}
```

## Step 4: Verify SSM Agent

SSH into your EC2 and check:

```bash
# Check if SSM agent is running
sudo systemctl status amazon-ssm-agent

# If not running, start it
sudo systemctl start amazon-ssm-agent
sudo systemctl enable amazon-ssm-agent
```

## Step 5: Update Workflow (Optional)

Edit `.github/workflows/deploy.yml` if needed:

```yaml
env:
  AWS_REGION: us-east-1  # Change to your region
```

## Step 6: Push and Deploy

```bash
git add .
git commit -m "Setup GitHub Actions deployment"
git push origin main
```

GitHub Actions will automatically:
1. Build your app
2. Upload to S3
3. Deploy to EC2
4. Restart the service

## Verify Deployment

1. **Check GitHub Actions:**
   - Go to **Actions** tab in GitHub
   - See the running workflow

2. **Check EC2 Logs:**
   ```bash
   sudo journalctl -u websocketapi -f
   ```

3. **Test Health Endpoint:**
   ```bash
   curl http://your-alb-or-ec2-ip/health
   ```

## Common Issues

### ❌ "No instances found"
- Check `EC2_INSTANCE_TAG` secret matches your EC2 Name tag exactly

### ❌ "SSM command failed"
- Verify EC2 has IAM role with `AmazonSSMManagedInstanceCore`
- Check SSM agent is running: `sudo systemctl status amazon-ssm-agent`

### ❌ "Access Denied to S3"
- Check EC2 IAM role has S3 read permissions
- Verify bucket name in GitHub secret is correct (no `s3://` prefix)

### ❌ "Application won't start"
```bash
# SSH into EC2 and check logs
sudo journalctl -u websocketapi -n 50

# Check if files were deployed
ls -la /var/www/websocketapi/

# Manually test
cd /var/www/websocketapi
dotnet WebSocketApi.dll
```

## Manual Deployment Trigger

You can also trigger deployments manually:

1. Go to **Actions** tab
2. Click **Deploy WebSocket API to EC2**
3. Click **Run workflow**
4. Select branch (main)
5. Click **Run workflow** button

## What Happens on Each Push

```
Push to main
  ↓
GitHub Actions starts
  ↓
Build .NET app
  ↓
Create deployment zip
  ↓
Upload to S3
  ↓
Send SSM command to EC2
  ↓
EC2 downloads from S3
  ↓
Stop service → Deploy → Start service
  ↓
Health check
  ↓
✅ Deployment complete!
```

## Next Steps

- Add SSL certificate to your ALB
- Update CORS settings with your frontend domain
- Set up CloudWatch alarms for monitoring
- Test WebSocket connection from Next.js frontend

Done! Your automated deployment is ready! 🚀
