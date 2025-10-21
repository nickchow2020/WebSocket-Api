# Complete Deployment Guide - Summary

This is a complete guide showing exactly what to do in each repository.

## Overview: Two Repos, Two Jobs

```
Infrastructure Repo (CDK)          Application Repo (This Repo)
        ↓                                   ↓
Creates AWS Resources              Deploys .NET Code
   (One Time)                      (Every Code Change)
```

---

## Part 1: Infrastructure Repo Setup

### What to Copy

From **this repo**, copy **ONLY** the `cdk-template/` folder to your infrastructure repo:

```bash
# In this API repo
cd WebSocketApi

# Copy to your infrastructure repo
cp -r cdk-template/* /path/to/your/infrastructure-repo/

# Your infrastructure repo should now have:
infrastructure-repo/
├── bin/
│   └── app.ts
├── lib/
│   └── websocket-api-stack.ts
├── package.json
├── tsconfig.json
├── cdk.json
├── .gitignore
└── README.md
```

### Deploy Infrastructure (Choose One Method)

#### Method A: Manual Deployment (Recommended) ⭐

```bash
# In your infrastructure repo
npm install

# Bootstrap CDK (first time only)
cdk bootstrap aws://YOUR-ACCOUNT-ID/us-east-1

# Preview changes
cdk diff --context environment=dev

# Deploy
cdk deploy --context environment=dev
```

**When to use:**
- ✅ Simple, straightforward
- ✅ Full control over when infrastructure changes
- ✅ No GitHub Actions setup needed

#### Method B: GitHub Actions Deployment (Optional)

If you want automated infrastructure deployment:

1. **Copy the GitHub Actions workflow:**
   ```bash
   mkdir -p .github/workflows
   cp cdk-template/github-workflows/cdk-deploy.yml .github/workflows/
   ```

2. **Add GitHub Secrets to your infrastructure repo:**
   - `AWS_ACCESS_KEY_ID`
   - `AWS_SECRET_ACCESS_KEY`

3. **Deploy via GitHub:**
   - Go to Actions tab
   - Run "Deploy CDK Infrastructure" workflow
   - Choose environment (dev/prod)

**When to use:**
- ✅ Team collaboration (no local AWS credentials needed)
- ✅ Consistent deployment environment
- ⚠️ Requires more setup

### After Infrastructure Deployment

Note these outputs (you'll need them for Part 2):

```
Outputs:
ALBDnsName = my-alb-xxxxx.us-east-1.elb.amazonaws.com
DeploymentBucket = websocket-api-deployments-dev-123456
InstanceTag = WebSocketApi
```

---

## Part 2: Application Repo Setup (This Repo)

### Add GitHub Secrets

Go to **this repo** → Settings → Secrets and variables → Actions

Add 4 secrets:

| Secret Name | Value | Where to Get It |
|------------|-------|-----------------|
| `AWS_ACCESS_KEY_ID` | `AKIAXXXXXXX` | IAM user for GitHub Actions |
| `AWS_SECRET_ACCESS_KEY` | `secret-key` | Same IAM user |
| `S3_DEPLOYMENT_BUCKET` | `websocket-api-deployments-dev-123456` | From CDK output |
| `EC2_INSTANCE_TAG` | `WebSocketApi` | From CDK output |

### Create IAM User for GitHub Actions

1. **Go to AWS Console → IAM → Users → Create User**
   - Name: `github-actions-websocket`
   - No console access needed

2. **Attach this policy:**
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

3. **Create Access Key:**
   - Security credentials → Create access key
   - Copy both values to GitHub secrets

### Deploy Application

```bash
# Just push to main branch
git push origin main
```

GitHub Actions automatically:
1. Builds .NET app
2. Uploads to S3
3. Deploys to EC2
4. Restarts service
5. Verifies health check

---

## Complete Workflow Diagram

```
┌─────────────────────────────────────────────────────────────┐
│ ONE-TIME SETUP (Infrastructure Repo)                        │
├─────────────────────────────────────────────────────────────┤
│                                                              │
│  1. Copy cdk-template/ to infrastructure repo               │
│  2. npm install                                              │
│  3. cdk bootstrap (first time only)                         │
│  4. cdk deploy --context environment=dev                    │
│     ↓                                                        │
│  Creates: EC2, ALB, S3, Security Groups, IAM Roles          │
│     ↓                                                        │
│  Outputs: ALB DNS, S3 Bucket, Instance Tag                  │
│                                                              │
└─────────────────────────────────────────────────────────────┘
                           ↓
┌─────────────────────────────────────────────────────────────┐
│ ONE-TIME SETUP (Application Repo - This Repo)               │
├─────────────────────────────────────────────────────────────┤
│                                                              │
│  1. Create IAM user for GitHub Actions                      │
│  2. Add 4 GitHub secrets (AWS keys, S3 bucket, EC2 tag)     │
│                                                              │
└─────────────────────────────────────────────────────────────┘
                           ↓
┌─────────────────────────────────────────────────────────────┐
│ ONGOING (Every Code Change)                                 │
├─────────────────────────────────────────────────────────────┤
│                                                              │
│  Push code to application repo                              │
│     ↓                                                        │
│  GitHub Actions runs automatically                          │
│     ↓                                                        │
│  Deploys to EC2 (created by infrastructure repo)            │
│                                                              │
└─────────────────────────────────────────────────────────────┘
```

---

## Quick Reference

### What Goes Where?

| File/Folder | Infrastructure Repo | Application Repo |
|------------|---------------------|------------------|
| `cdk-template/` | ✅ Copy here | ❌ Leave in API repo |
| `.NET code` | ❌ Don't copy | ✅ Stays here |
| `.github/workflows/deploy.yml` | ❌ Don't copy | ✅ Already here |
| `cdk-deploy.yml` (optional) | ✅ If using GH Actions | ❌ Not needed |

### Deployment Commands

| Task | Repo | Command |
|------|------|---------|
| **Deploy Infrastructure** | Infrastructure | `cdk deploy` |
| **Deploy Application** | Application | `git push origin main` |
| **Update Infrastructure** | Infrastructure | `cdk deploy` |
| **Update Application** | Application | `git push origin main` |

### URLs After Deployment

| What | URL | Use For |
|------|-----|---------|
| **Health Check** | `http://your-alb-dns/health` | Testing |
| **WebSocket** | `ws://your-alb-dns/ws` | Next.js connection |
| **WebSocket (SSL)** | `wss://your-domain.com/ws` | Production (after SSL) |

---

## Testing the Complete Setup

### 1. Test Infrastructure

```bash
# After CDK deploy
curl http://YOUR-ALB-DNS/health
# Should return: Healthy (or nothing yet, app not deployed)
```

### 2. Test Application Deployment

```bash
# In this repo, trigger deployment
git commit --allow-empty -m "Test deployment"
git push origin main

# Check GitHub Actions tab for progress
# Wait 2-3 minutes

# Test again
curl http://YOUR-ALB-DNS/health
# Should return: Healthy
```

### 3. Test WebSocket

```javascript
// In browser console
const ws = new WebSocket('ws://YOUR-ALB-DNS/ws');
ws.onopen = () => {
  console.log('Connected!');
  ws.send('start_stream');
};
ws.onmessage = (e) => {
  console.log('Received:', JSON.parse(e.data));
};
```

### 4. Update Next.js Frontend

```typescript
// In your Next.js dashboard component
const ws = new WebSocket('ws://YOUR-ALB-DNS/ws');
```

---

## Troubleshooting

### Infrastructure Deployment Fails

```bash
# Check CloudFormation events
aws cloudformation describe-stack-events \
  --stack-name websocket-api-dev

# Check CDK diff before deploying
cdk diff --context environment=dev
```

### Application Deployment Fails

**Check GitHub Actions logs:**
1. Go to Actions tab
2. Click on failed workflow
3. Expand failed step

**Common issues:**
- Wrong S3 bucket name in secrets
- Wrong EC2 instance tag in secrets
- IAM user missing permissions
- EC2 doesn't have SSM agent running

### Health Check Fails

```bash
# SSH into EC2 via SSM
aws ssm start-session --target i-YOUR-INSTANCE-ID

# Check application logs
sudo journalctl -u websocketapi -f

# Check Nginx
sudo systemctl status nginx

# Test locally
curl http://localhost:5000/health
```

---

## Cost Breakdown

**Monthly costs (single environment):**
- EC2 t3.small: ~$15
- ALB: ~$20
- NAT Gateway: ~$32
- S3: ~$1
- **Total: ~$68/month**

**To reduce costs:**
- Use t3.micro instead of t3.small (~$7/month)
- Remove NAT Gateway (use public subnet for EC2)
- Stop EC2 when not in use (dev only)

---

## Summary Checklist

### Infrastructure Repo
- [ ] Copy `cdk-template/` contents
- [ ] Run `npm install`
- [ ] Run `cdk bootstrap` (first time)
- [ ] Run `cdk deploy --context environment=dev`
- [ ] Note ALB DNS, S3 bucket, instance tag from outputs

### Application Repo (This Repo)
- [ ] Create IAM user for GitHub Actions
- [ ] Add 4 GitHub secrets
- [ ] Push to main branch
- [ ] Verify deployment in Actions tab
- [ ] Test `/health` endpoint
- [ ] Test WebSocket connection
- [ ] Update Next.js with WebSocket URL

### Done! 🎉

Your WebSocket API is now:
- ✅ Deployed on AWS EC2
- ✅ Behind Application Load Balancer
- ✅ Auto-deploying on every push
- ✅ Ready for your Next.js frontend

---

## Need Help?

- **CDK Issues:** See `cdk-template/README.md`
- **GitHub Actions:** See `GITHUB-ACTIONS-SETUP.md`
- **SSL Setup:** See `ALB-SSL-SETUP.md`
- **General Deployment:** See `CDK-SETUP.md`
