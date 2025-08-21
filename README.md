# MagicLinkDemo

A simple ASP.NET Core 8.0 web application demonstrating passwordless authentication using magic links sent via email.

## Features

- **Passwordless Authentication**: Users log in using magic links sent to their email
- **Email Integration**: AWS SES for reliable email delivery
- **Redis Caching**: Optional Redis support for scalability
- **Rate Limiting**: Built-in protection against spam requests
- **Cookie Authentication**: Secure session management
- **Health Checks**: Basic monitoring endpoint

## Tech Stack

- **Backend**: ASP.NET Core 8.0
- **Email**: Amazon SES (Simple Email Service)
- **Caching**: Redis (optional) + In-Memory Cache
- **Authentication**: Cookie-based authentication
- **Deployment**: Railway-ready with Docker support

## Quick Start

1. **Clone the repository**
   ```bash
   git clone https://github.com/RachelSchreiber/MagicLinkDemo.git
   cd MagicLinkDemo
   ```

2. **Set up environment variables**
   ```
   AWS_ACCESS_KEY_ID=your_aws_access_key
   AWS_SECRET_ACCESS_KEY=your_aws_secret_key
   AWS_DEFAULT_REGION=us-east-1
   REDIS_CONNECTION_STRING=your_redis_url (optional)
   ```

3. **Run the application**
   ```bash
   dotnet run --urls http://localhost:5000
   ```

4. **Visit** `http://localhost:5000` to test the magic link authentication

## Environment Variables

| Variable | Required | Description |
|----------|----------|-------------|
| `AWS_ACCESS_KEY_ID` | Yes | AWS access key for SES |
| `AWS_SECRET_ACCESS_KEY` | Yes | AWS secret key for SES |
| `AWS_DEFAULT_REGION` | No | AWS region (default: us-east-1) |
| `REDIS_CONNECTION_STRING` | No | Redis connection for caching |
| `PORT` | No | Server port (default: 8080) |

## Deployment

The app is configured for easy deployment on Railway with included `railway.json` and `Dockerfile`.
