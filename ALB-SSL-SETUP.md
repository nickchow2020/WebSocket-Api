# Application Load Balancer with SSL Setup

The CDK infrastructure includes an ALB, but you may want to add SSL/HTTPS for production.

## Current ALB Setup (Included in CDK)

Your CDK creates:
- ✅ Application Load Balancer (public-facing)
- ✅ Target Group pointing to EC2 instance
- ✅ Health checks on `/health`
- ✅ Sticky sessions (required for WebSocket)
- ✅ HTTP listener on port 80

## Adding HTTPS/WSS Support

For production, you should use HTTPS/WSS instead of HTTP/WS.

### Option 1: Add HTTPS Listener to CDK (Recommended)

**Prerequisites:**
- Domain name (e.g., `api.yourdomain.com`)
- SSL certificate in AWS Certificate Manager (ACM)

**Update your CDK code:**

```typescript
import * as acm from 'aws-cdk-lib/aws-certificatemanager';
import * as route53 from 'aws-cdk-lib/aws-route53';
import * as targets from 'aws-cdk-lib/aws-route53-targets';

export class WebSocketApiStack extends cdk.Stack {
  constructor(scope: cdk.App, id: string, props?: cdk.StackProps) {
    super(scope, id, props);

    // ... existing code (VPC, EC2, etc.) ...

    // Get or create SSL certificate
    const certificate = acm.Certificate.fromCertificateArn(
      this,
      'Certificate',
      'arn:aws:acm:us-east-1:YOUR-ACCOUNT:certificate/YOUR-CERT-ID'
    );

    // OR request a new certificate
    // const certificate = new acm.Certificate(this, 'Certificate', {
    //   domainName: 'api.yourdomain.com',
    //   validation: acm.CertificateValidation.fromDns(),
    // });

    // Create ALB
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

    // HTTP listener - redirect to HTTPS
    alb.addListener('HttpListener', {
      port: 80,
      defaultAction: elbv2.ListenerAction.redirect({
        protocol: 'HTTPS',
        port: '443',
        permanent: true,
      }),
    });

    // HTTPS listener
    alb.addListener('HttpsListener', {
      port: 443,
      protocol: elbv2.ApplicationProtocol.HTTPS,
      certificates: [certificate],
      defaultTargetGroups: [targetGroup],
    });

    // Route 53 (if you manage DNS in AWS)
    // const hostedZone = route53.HostedZone.fromLookup(this, 'HostedZone', {
    //   domainName: 'yourdomain.com',
    // });

    // new route53.ARecord(this, 'AliasRecord', {
    //   zone: hostedZone,
    //   recordName: 'api',
    //   target: route53.RecordTarget.fromAlias(new targets.LoadBalancerTarget(alb)),
    // });

    // Outputs
    new cdk.CfnOutput(this, 'WebSocketURL', {
      value: `wss://api.yourdomain.com/ws`,
      description: 'WebSocket URL',
    });
  }
}
```

### Option 2: Request Certificate via ACM Console

**Step 1: Request Certificate**
```bash
# Go to AWS Certificate Manager
# Click "Request a certificate"
# Enter domain: api.yourdomain.com
# Choose DNS validation
# Add CNAME record to your DNS
# Wait for validation (5-30 minutes)
```

**Step 2: Update CDK with Certificate ARN**
```typescript
const certificate = acm.Certificate.fromCertificateArn(
  this,
  'Certificate',
  'arn:aws:acm:us-east-1:123456789:certificate/abc-123-def'
);
```

**Step 3: Add HTTPS Listener (code above)**

**Step 4: Point DNS to ALB**
```bash
# In your DNS provider (Route53, Cloudflare, etc.)
# Create A/CNAME record:
# Name: api.yourdomain.com
# Value: your-alb-xxxxx.us-east-1.elb.amazonaws.com
```

## ALB Security Group

Make sure your ALB security group allows HTTPS:

```typescript
const albSecurityGroup = new ec2.SecurityGroup(this, 'AlbSG', {
  vpc,
  description: 'ALB Security Group',
  allowAllOutbound: true,
});

albSecurityGroup.addIngressRule(
  ec2.Peer.anyIpv4(),
  ec2.Port.tcp(80),
  'Allow HTTP'
);

albSecurityGroup.addIngressRule(
  ec2.Peer.anyIpv4(),
  ec2.Port.tcp(443),
  'Allow HTTPS'
);

const alb = new elbv2.ApplicationLoadBalancer(this, 'ALB', {
  vpc,
  internetFacing: true,
  securityGroup: albSecurityGroup,
});
```

## WebSocket Configuration for ALB

**Important settings already in your CDK:**

```typescript
const targetGroup = new elbv2.ApplicationTargetGroup(this, 'TargetGroup', {
  // ... other settings ...

  // ✅ Sticky sessions - REQUIRED for WebSocket
  stickinessCookieDuration: cdk.Duration.hours(1),

  // ✅ Health checks
  healthCheck: {
    path: '/health',
    interval: cdk.Duration.seconds(30),
    healthyThresholdCount: 2,
    unhealthyThresholdCount: 3,
    timeout: cdk.Duration.seconds(5),
  },

  // ✅ Connection draining
  deregistrationDelay: cdk.Duration.seconds(30),
});
```

**Additional recommended attributes:**

```typescript
targetGroup.setAttribute('stickiness.enabled', 'true');
targetGroup.setAttribute('stickiness.type', 'lb_cookie');
targetGroup.setAttribute('deregistration_delay.timeout_seconds', '30');
```

## Testing Your ALB

### Test HTTP (before SSL)
```bash
# Get ALB DNS from CDK output
curl http://your-alb-xxxxx.us-east-1.elb.amazonaws.com/health
# Should return: Healthy

# Test WebSocket
# In browser console:
const ws = new WebSocket('ws://your-alb-xxxxx.us-east-1.elb.amazonaws.com/ws');
ws.onopen = () => ws.send('start_stream');
ws.onmessage = (e) => console.log(JSON.parse(e.data));
```

### Test HTTPS (after SSL)
```bash
curl https://api.yourdomain.com/health

# WebSocket with SSL
const ws = new WebSocket('wss://api.yourdomain.com/ws');
ws.onopen = () => ws.send('start_stream');
ws.onmessage = (e) => console.log(JSON.parse(e.data));
```

## Update CORS After Adding Domain

Edit `appsettings.Production.json`:

```json
{
  "CorsSettings": {
    "AllowedOrigins": [
      "https://your-frontend-domain.com"
    ]
  }
}
```

Then redeploy:
```bash
git commit -am "Update CORS for production domain"
git push origin main
```

## Monitoring ALB

**CloudWatch Metrics to monitor:**
- Target Response Time
- Healthy Host Count
- Unhealthy Host Count
- Request Count
- HTTP 5XX errors

**Set up alarms in CDK:**

```typescript
import * as cloudwatch from 'aws-cdk-lib/aws-cloudwatch';
import * as sns from 'aws-cdk-lib/aws-sns';

// Create SNS topic for alarms
const alarmTopic = new sns.Topic(this, 'AlarmTopic', {
  displayName: 'WebSocket API Alarms',
});

// Unhealthy host alarm
new cloudwatch.Alarm(this, 'UnhealthyHostAlarm', {
  metric: targetGroup.metricUnhealthyHostCount(),
  threshold: 1,
  evaluationPeriods: 2,
  alarmDescription: 'Alert when instance becomes unhealthy',
  alarmName: 'WebSocketApi-UnhealthyHost',
});

// Response time alarm
new cloudwatch.Alarm(this, 'ResponseTimeAlarm', {
  metric: targetGroup.metricTargetResponseTime(),
  threshold: 1,
  evaluationPeriods: 2,
  alarmDescription: 'Alert when response time > 1s',
  alarmName: 'WebSocketApi-HighResponseTime',
});
```

## Cost Considerations

**ALB Pricing (us-east-1):**
- ALB Hour: ~$0.0225/hour (~$16/month)
- LCU (Load Balancer Capacity Units): ~$0.008/hour
- **Total ALB cost: ~$20-25/month**

**Tips to reduce costs:**
- Use ALB for production only
- For dev/testing, you can point directly to EC2 public IP
- Consider using Network Load Balancer (NLB) if you only need TCP (cheaper)

## Summary

✅ **Your CDK already includes:**
- Application Load Balancer
- Target Group with health checks
- Sticky sessions for WebSocket
- HTTP listener on port 80

🔧 **To add for production:**
- Request SSL certificate in ACM
- Add HTTPS listener (port 443)
- Redirect HTTP to HTTPS
- Point your domain to ALB
- Update CORS settings

Your ALB is ready - just add SSL for production! 🚀
