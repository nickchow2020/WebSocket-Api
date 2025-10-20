# CloudFormation Deployment Guide

This guide provides the necessary information to deploy the WebSocket API using AWS CloudFormation from your infrastructure repository.

## Application Details

**Repository:** https://github.com/nickchow2020/WebSocket-Api.git
**Runtime:** .NET 8.0
**Port:** 5000 (internal), 80/443 (external via ALB/Nginx)
**Health Check Endpoint:** `/health`
**WebSocket Endpoint:** `/ws`

## User Data Script

A ready-to-use EC2 User Data script is available at: `scripts/user-data.sh`

This script will:
- Install .NET 8 Runtime
- Clone and build the application
- Set up systemd service
- Configure Nginx as reverse proxy
- Start the application automatically

## Required Resources

### 1. EC2 Instance

**Recommended Specifications:**
- AMI: Ubuntu 22.04 LTS (ami-0c7217cdde317cfec in us-east-1)
- Instance Type: t3.small or t3.medium
- Storage: 20GB gp3
- User Data: Use `scripts/user-data.sh`

### 2. Security Group Rules

**Inbound:**
- Port 80 (HTTP) - 0.0.0.0/0 or ALB Security Group
- Port 443 (HTTPS) - 0.0.0.0/0 or ALB Security Group
- Port 22 (SSH) - Your IP/Bastion (for troubleshooting)

**Outbound:**
- Allow all (for updates and GitHub access)

### 3. IAM Role (Optional but Recommended)

Attach an IAM role to EC2 for:
- CloudWatch Logs access
- Systems Manager (SSM) access for remote management
- Secrets Manager (if using for configuration)

**Suggested Managed Policies:**
- `CloudWatchAgentServerPolicy`
- `AmazonSSMManagedInstanceCore`

### 4. Application Load Balancer (Recommended)

**Target Group Configuration:**
- Protocol: HTTP
- Port: 80
- Health Check Path: `/health`
- Health Check Interval: 30 seconds
- Healthy Threshold: 2
- Unhealthy Threshold: 3
- Timeout: 5 seconds
- Success Codes: 200

**Listener Rules:**
- HTTP (80): Redirect to HTTPS
- HTTPS (443): Forward to Target Group
- Sticky Sessions: Enabled (for WebSocket)
- Stickiness Duration: 1 hour

**Important:** Enable sticky sessions for WebSocket connections to work properly with multiple instances.

### 5. Target Group Attributes for WebSocket

```yaml
TargetGroupAttributes:
  - Key: stickiness.enabled
    Value: true
  - Key: stickiness.type
    Value: lb_cookie
  - Key: stickiness.lb_cookie.duration_seconds
    Value: 3600
  - Key: deregistration_delay.timeout_seconds
    Value: 30
```

## Environment Variables

Set these via User Data or Parameter Store:

```bash
ASPNETCORE_ENVIRONMENT=Production
ASPNETCORE_URLS=http://0.0.0.0:5000
```

## Configuration Management

### Option 1: Use Parameter Store / Secrets Manager

Store sensitive configuration in AWS Systems Manager Parameter Store:

```bash
# Example parameters
/websocketapi/production/cors-origins
/websocketapi/production/database-connection (if needed later)
```

Update your CloudFormation to inject these as environment variables.

### Option 2: Mount Configuration via S3

Upload `appsettings.Production.json` to S3 and download during instance initialization:

```bash
aws s3 cp s3://your-config-bucket/appsettings.Production.json /var/www/websocketapi/
```

## Sample CloudFormation Resource Properties

### EC2 Instance

```yaml
WebSocketApiInstance:
  Type: AWS::EC2::Instance
  Properties:
    ImageId: ami-0c7217cdde317cfec  # Ubuntu 22.04 LTS
    InstanceType: t3.small
    IamInstanceProfile: !Ref WebSocketApiInstanceProfile
    SecurityGroupIds:
      - !Ref WebSocketApiSecurityGroup
    SubnetId: !Ref PrivateSubnet
    UserData:
      Fn::Base64: !Sub |
        #!/bin/bash
        # Use the content from scripts/user-data.sh
        ${UserDataScript}
    Tags:
      - Key: Name
        Value: WebSocketApi
```

### Security Group

```yaml
WebSocketApiSecurityGroup:
  Type: AWS::EC2::SecurityGroup
  Properties:
    GroupDescription: Security group for WebSocket API
    VpcId: !Ref VPC
    SecurityGroupIngress:
      - IpProtocol: tcp
        FromPort: 80
        ToPort: 80
        SourceSecurityGroupId: !Ref ALBSecurityGroup
      - IpProtocol: tcp
        FromPort: 443
        ToPort: 443
        SourceSecurityGroupId: !Ref ALBSecurityGroup
    Tags:
      - Key: Name
        Value: WebSocketApi-SG
```

### Application Load Balancer

```yaml
WebSocketApiALB:
  Type: AWS::ElasticLoadBalancingV2::LoadBalancer
  Properties:
    Name: WebSocketApi-ALB
    Subnets:
      - !Ref PublicSubnet1
      - !Ref PublicSubnet2
    SecurityGroups:
      - !Ref ALBSecurityGroup
    Tags:
      - Key: Name
        Value: WebSocketApi-ALB

WebSocketApiTargetGroup:
  Type: AWS::ElasticLoadBalancingV2::TargetGroup
  Properties:
    Name: WebSocketApi-TG
    Port: 80
    Protocol: HTTP
    VpcId: !Ref VPC
    HealthCheckPath: /health
    HealthCheckProtocol: HTTP
    HealthCheckIntervalSeconds: 30
    HealthCheckTimeoutSeconds: 5
    HealthyThresholdCount: 2
    UnhealthyThresholdCount: 3
    TargetGroupAttributes:
      - Key: stickiness.enabled
        Value: true
      - Key: stickiness.type
        Value: lb_cookie
      - Key: deregistration_delay.timeout_seconds
        Value: 30
    Targets:
      - Id: !Ref WebSocketApiInstance
        Port: 80
```

## Auto Scaling Group (Optional)

For high availability and scaling:

```yaml
WebSocketApiLaunchTemplate:
  Type: AWS::EC2::LaunchTemplate
  Properties:
    LaunchTemplateName: WebSocketApi-LT
    LaunchTemplateData:
      ImageId: ami-0c7217cdde317cfec
      InstanceType: t3.small
      IamInstanceProfile:
        Arn: !GetAtt WebSocketApiInstanceProfile.Arn
      SecurityGroupIds:
        - !Ref WebSocketApiSecurityGroup
      UserData:
        Fn::Base64: !Sub |
          ${UserDataScript}

WebSocketApiASG:
  Type: AWS::AutoScaling::AutoScalingGroup
  Properties:
    LaunchTemplate:
      LaunchTemplateId: !Ref WebSocketApiLaunchTemplate
      Version: !GetAtt WebSocketApiLaunchTemplate.LatestVersionNumber
    MinSize: 1
    MaxSize: 4
    DesiredCapacity: 2
    TargetGroupARNs:
      - !Ref WebSocketApiTargetGroup
    VPCZoneIdentifier:
      - !Ref PrivateSubnet1
      - !Ref PrivateSubnet2
    HealthCheckType: ELB
    HealthCheckGracePeriod: 300
```

## CORS Configuration

Update `appsettings.Production.json` with your frontend domain:

```json
{
  "CorsSettings": {
    "AllowedOrigins": [
      "https://your-frontend-domain.com",
      "https://www.your-frontend-domain.com"
    ]
  }
}
```

You can:
1. Bake this into the AMI
2. Inject via Parameter Store
3. Download from S3 during User Data execution

## Outputs to Reference

After deployment, you'll need these values for your frontend:

```yaml
Outputs:
  WebSocketApiURL:
    Description: WebSocket API URL
    Value: !Sub 'wss://${WebSocketApiALB.DNSName}/ws'
    Export:
      Name: !Sub '${AWS::StackName}-WebSocketURL'

  HealthCheckURL:
    Description: Health Check URL
    Value: !Sub 'https://${WebSocketApiALB.DNSName}/health'
    Export:
      Name: !Sub '${AWS::StackName}-HealthCheckURL'

  ALBDNSName:
    Description: ALB DNS Name
    Value: !GetAtt WebSocketApiALB.DNSName
    Export:
      Name: !Sub '${AWS::StackName}-ALBDNSName'
```

## Post-Deployment Verification

1. **Check instance is running:**
   ```bash
   aws ec2 describe-instances --filters "Name=tag:Name,Values=WebSocketApi"
   ```

2. **Verify health check:**
   ```bash
   curl https://your-alb-dns/health
   # Should return: Healthy
   ```

3. **Test WebSocket connection:**
   ```javascript
   const ws = new WebSocket('wss://your-alb-dns/ws');
   ws.onopen = () => ws.send('start_stream');
   ws.onmessage = (e) => console.log(JSON.parse(e.data));
   ```

## Monitoring & Logging

### CloudWatch Logs

Add to User Data to send logs to CloudWatch:

```bash
# Install CloudWatch agent
wget https://s3.amazonaws.com/amazoncloudwatch-agent/ubuntu/amd64/latest/amazon-cloudwatch-agent.deb
dpkg -i amazon-cloudwatch-agent.deb

# Configure to send application logs
cat > /opt/aws/amazon-cloudwatch-agent/etc/config.json <<EOF
{
  "logs": {
    "logs_collected": {
      "files": {
        "collect_list": [
          {
            "file_path": "/var/log/user-data.log",
            "log_group_name": "/aws/ec2/websocketapi/userdata",
            "log_stream_name": "{instance_id}"
          }
        ]
      }
    }
  }
}
EOF

/opt/aws/amazon-cloudwatch-agent/bin/amazon-cloudwatch-agent-ctl \
  -a fetch-config \
  -m ec2 \
  -c file:/opt/aws/amazon-cloudwatch-agent/etc/config.json \
  -s
```

### Alarms

Create CloudWatch Alarms for:
- Target Unhealthy Host Count
- Instance CPU Utilization
- ALB 5XX errors
- ALB Target Response Time

## Updating the Application

To deploy updates:

1. Push changes to GitHub
2. SSH into instance (or use SSM)
3. Run update script:
   ```bash
   cd /opt/websocketapi
   git pull origin main
   dotnet publish WebSocketApi/WebSocketApi.csproj -c Release -o /var/www/websocketapi
   systemctl restart websocketapi
   ```

Or create a CodeDeploy configuration for automated deployments.

## Cost Estimation

**Minimal Setup (Single t3.small):**
- EC2 t3.small: ~$15/month
- ALB: ~$20/month
- Data Transfer: Variable
- **Total: ~$35-50/month**

**Production Setup (Auto Scaling, 2-4 instances):**
- EC2 t3.small (2-4): ~$30-60/month
- ALB: ~$20/month
- Data Transfer: Variable
- CloudWatch: ~$5/month
- **Total: ~$60-100/month**

## Security Checklist

- ✅ Use HTTPS/WSS in production (not HTTP/WS)
- ✅ Restrict Security Group to only necessary ports
- ✅ Use IAM roles instead of access keys
- ✅ Enable ALB access logs
- ✅ Configure CloudWatch alarms
- ✅ Use Systems Manager Session Manager instead of SSH
- ✅ Enable encryption at rest and in transit
- ✅ Regularly update the OS and .NET runtime

## Next Steps

1. Add this repository URL to your infrastructure repo CloudFormation
2. Reference `scripts/user-data.sh` in your EC2 User Data
3. Configure your ALB with sticky sessions
4. Update CORS settings with your frontend domain
5. Set up CloudWatch monitoring
6. Configure your Next.js app to use `wss://your-alb-domain/ws`
