# Git Secret Removal Commands

## One-Line Command to Remove "client info.txt" from Git History

```bash
git filter-branch --force --index-filter 'git rm --cached --ignore-unmatch "client info.txt"' --prune-empty --tag-name-filter cat -- --all && git push origin --force --all
```

## Step-by-Step Alternative (if you prefer to see each step)

```bash
# Step 1: Remove file from history
git filter-branch --force --index-filter 'git rm --cached --ignore-unmatch "client info.txt"' --prune-empty --tag-name-filter cat -- --all

# Step 2: Force push to overwrite remote history
git push origin --force --all
```

## ⚠️ CRITICAL SECURITY STEPS AFTER RUNNING THE COMMAND

1. **Immediately revoke the exposed secret** in Google Cloud Console
2. **Generate new OAuth credentials** 
3. **Update your application** with new credentials using user secrets
4. **Add to .gitignore** to prevent future accidents

## Alternative: Modern Git Command (Git 2.23+)

```bash
git filter-repo --path "client info.txt" --invert-paths && git push origin --force --all
```

**Note**: Requires `git-filter-repo` to be installed: `pip install git-filter-repo`