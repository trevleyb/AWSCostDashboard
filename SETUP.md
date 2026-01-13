# Setting up the SSO Access

## Create the folder
`mkdir -p ~/.aws`

## Create the config file
```
cat > ~/.aws/config << 'EOF'
[sso-session company]
sso_start_url = https://company.awsapps.com/start
sso_region = ap-southeast-2
sso_registration_scopes = sso:account:access

[profile master]
sso_session = company
sso_account_id = <id>
sso_role_name = AdministratorAccess
region = us-east-1
EOF
```

## Verify the file
```
cat ~/.aws/config
```

## Sign in to create the token
```
aws sso login --profile master
```

## Test It
```
aws sts get-caller-identity --profile master
```
