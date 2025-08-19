# 🔧 Git Setup & Credentials Configuration

## 📥 Step 1: Install Git

### Download and Install Git:
1. **Go to:** https://git-scm.com/download/win
2. **Download** Git for Windows (64-bit)
3. **Run the installer** with these settings:
   - ✅ **Use Git from the command line** (important!)
   - ✅ **Use bundled OpenSSH**
   - ✅ **Use the native Windows Secure Channel library**
   - ✅ **Checkout Windows-style, commit Unix-style line endings**
   - ✅ **Use Windows' default console window**
4. **Restart VS Code** after installation

---

## 🔑 Step 2: Configure Git Credentials

### Option A: Global Configuration (Recommended)

Open **PowerShell** or **Command Prompt** and run:

```powershell
# Set your name (will appear in commits)
git config --global user.name "Your Full Name"

# Set your email (must match GitHub email)
git config --global user.email "your-email@gmail.com"

# Verify configuration
git config --global --list
```

### Option B: GitHub Account Setup

If you don't have a GitHub account:

1. **Go to:** https://github.com
2. **Sign up** with your email
3. **Verify your email** address
4. **Use the same email** in Git config above

---

## 🔐 Step 3: Authentication Methods

### Method 1: Personal Access Token (Recommended)

1. **GitHub.com** → **Settings** → **Developer settings** → **Personal access tokens** → **Tokens (classic)**
2. **Generate new token** with these permissions:
   - ✅ `repo` (Full control of private repositories)
   - ✅ `workflow` (Update GitHub Action workflows)
3. **Copy the token** (you won't see it again!)
4. **When pushing to GitHub:**
   - Username: Your GitHub username
   - Password: Your personal access token (not your GitHub password)

### Method 2: GitHub CLI (Alternative)

```powershell
# Install GitHub CLI (optional)
winget install --id GitHub.cli

# Authenticate with GitHub
gh auth login
```

---

## 🚀 Step 4: Initialize Your Repository

After Git is installed and configured:

```powershell
# Navigate to your project
cd "C:\Users\רחל\MagicLinkDemo"

# Initialize Git repository
git init

# Add all files
git add .

# Create first commit
git commit -m "Initial commit - Magic Link Demo"

# Add GitHub remote (replace with your GitHub repo URL)
git remote add origin https://github.com/YOUR_USERNAME/magic-link-demo.git

# Push to GitHub
git branch -M main
git push -u origin main
```

---

## 🔍 Step 5: Verify Git Configuration

```powershell
# Check Git version
git --version

# Check configuration
git config --global --list

# Check repository status
git status
```

---

## 🚨 Common Issues & Solutions

### Issue: "git is not recognized"
**Solution:** 
- Restart VS Code/PowerShell after Git installation
- Add Git to PATH manually if needed

### Issue: Authentication failed
**Solutions:**
- Use Personal Access Token instead of password
- Enable 2FA on GitHub account
- Use `gh auth login` for easier authentication

### Issue: Permission denied
**Solutions:**
- Check repository permissions
- Verify email matches GitHub account
- Use correct Personal Access Token

---

## 📋 Quick Setup Commands

**Copy and paste these commands after installing Git:**

```powershell
# Configure Git (replace with your info)
git config --global user.name "Rachel Developer"
git config --global user.email "r0583214262@gmail.com"

# Initialize repository
git init
git add .
git commit -m "Magic Link Authentication Demo"

# Connect to GitHub (you'll create this repo next)
git remote add origin https://github.com/YOUR_USERNAME/magic-link-demo.git
git branch -M main
git push -u origin main
```

---

## 🎯 Next Steps After Git Setup

1. **Create GitHub repository:** https://github.com/new
2. **Name it:** `magic-link-demo`
3. **Make it public** (for easy interview access)
4. **Don't initialize** with README (you already have files)
5. **Copy the repository URL**
6. **Run the push commands above**

---

## 🔒 Security Best Practices

### ✅ Do:
- Use Personal Access Tokens
- Keep tokens secure
- Use strong passwords
- Enable 2FA on GitHub

### ❌ Don't:
- Share your tokens
- Commit tokens to code
- Use weak passwords
- Disable 2FA

---

**Ready to install Git? Go to https://git-scm.com/download/win and download it now!**
