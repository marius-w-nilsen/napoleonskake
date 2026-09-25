# 🍰 Napoleonskake Friday Score

A Microsoft Teams bot that keeps score of the Friday Napoleonskake.

Every Friday at lunch it posts a scoring card into the channel. Everyone picks a score from 1 to 10, optionally with a comment, and the bot tracks it over time: today's average, the trend across Fridays, the best and worst Friday ever, and who the most generous and harshest critics are.

## What it does

| You say (or click)      | The bot does                                                                 |
|-------------------------|------------------------------------------------------------------------------|
| Friday 12:00 (automatic) | Posts the scoring card with a dropdown (1–10) and a comment box. Scoring opens |
| `score 8 nice and crispy` or `8` | Records your score for today. Scoring again replaces it. Only works while scoring is open |
| `results`               | Today's average, a verdict, and everyone's scores with comments              |
| `history`               | The last 8 Fridays as a bar chart plus the all-time average and trend        |
| `leaderboard`           | Best/worst Friday, most generous scorer, harshest critic, most dedicated     |
| `card`                  | Posts the scoring card again (for a chat that missed the automatic one)     |
| `help`                  | Command list                                                                 |
| Friday 17:00 (automatic) | Locks the card and posts the final results. Scoring closes                 |

The scoring card updates itself in place as scores come in (count, running average, who scored last), so the channel gets a live tally without spam. In channels, @mention the bot: `@Napoleonskake score 8`.

**Scoring window:** scores are only accepted on Fridays between 12:00 and 17:00 (Europe/Oslo). Outside that window the bot replies with when scoring opens, or that it has closed, and nothing is recorded. `results`, `history` and `leaderboard` work any time; on other days `results` shows the most recent Friday.

## Project layout

```
src/NapoleonBot/
  Program.cs                      ASP.NET Core host, /api/messages endpoint
  Bot/NapoleonTeamsBot.cs         Teams activity handler: commands, card submits, install events
  Bot/Commands.cs                 Text command parser
  Cards/CardFactory.cs            Adaptive Cards and all user-facing wording
  Data/ScoreStore.cs              SQLite storage (scores, posted cards, conversations)
  Services/FridayScheduler.cs     Background service that posts the card on cake day
  Services/ScoringCardPublisher.cs Posts and refreshes the scoring card
appPackage/                       Teams app manifest and icons
scripts/                          Icon generator, app-package builder, Azure deploy
tests/NapoleonBot.Tests/          xunit tests for parser, calendar, store, cards
Dockerfile, docker-compose.yml    Self-hosting with Docker (TLS handled by your own tunnel or proxy)
```

Stack: .NET 10, Bot Framework SDK 4.23, Microsoft.Data.Sqlite, Adaptive Cards 1.5. No Azure resources are needed other than the Azure Bot registration itself (free F0 tier). The database is a single SQLite file in `db/`.

## Running locally

```powershell
dotnet test
dotnet run --project src/NapoleonBot
```

The bot listens on `http://localhost:3978`. Without app credentials it accepts unauthenticated requests, which is what the [Bot Framework Emulator](https://github.com/microsoft/BotFramework-Emulator/releases) sends. Point the emulator at `http://localhost:3978/api/messages` and type `help`.

Once you have stored real credentials in user secrets (step 2 below), the default profile loads them and rejects the Emulator's unsigned requests with 401. The Emulator cannot sign requests for a single-tenant registration, so use the `Emulator` launch profile instead. It skips user secrets, listens on port 3979, and uses a separate database (`db/emulator.db`) so Emulator test scores never mix with real ones:

```powershell
dotnet run --project src/NapoleonBot --launch-profile Emulator
```

Point the Emulator at `http://localhost:3979/api/messages` and leave the app id and password empty. Both profiles can run at the same time.

To test the Friday flow without waiting for Friday, temporarily set `Schedule:CakeDay` to today, `Schedule:PostTime` to a minute from now and `Schedule:ScoringCloses` a few minutes after that, e.g.

```powershell
$env:Schedule__CakeDay = "Thursday"; $env:Schedule__PostTime = "14:05"; $env:Schedule__ScoringCloses = "14:10"
dotnet run --project src/NapoleonBot
```

The card is posted to every conversation the bot has been added to (or has been talked to in). At closing time the card is locked and the final results are posted. Both steps happen at most once per day and conversation, so restarting the bot mid-Friday is safe.

## Deploying to Teams

You need an Azure Bot registration so Teams can reach your bot, and a public HTTPS URL for the bot.

### 1. Register the bot in Azure

```powershell
az login
$rg = "rg-napoleonskake"
az group create -n $rg -l norwayeast

# App registration (single tenant) and a client secret
$appId = az ad app create --display-name "Napoleonskake Bot" --sign-in-audience AzureADMyOrg --query appId -o tsv
az ad sp create --id $appId        # required: without a service principal the bot cannot get a token to reply (AADSTS7000229)
$secret = az ad app credential reset --id $appId --append --display-name bot --years 2 --query password -o tsv
$tenantId = az account show --query tenantId -o tsv

# Azure Bot resource (free tier), pointing at your public endpoint
az bot create -g $rg -n napoleonskake-bot --app-type SingleTenant --appid $appId --tenant-id $tenantId `
  --endpoint "https://<your-public-host>/api/messages" --sku F0
az bot msteams create -g $rg -n napoleonskake-bot
```

### 2. Configure the bot

Locally, use user secrets so nothing lands in git:

```powershell
cd src/NapoleonBot
dotnet user-secrets set MicrosoftAppType SingleTenant
dotnet user-secrets set MicrosoftAppId $appId
dotnet user-secrets set MicrosoftAppPassword $secret
dotnet user-secrets set MicrosoftAppTenantId $tenantId
```

In production set the same four values as environment variables (`MicrosoftAppType`, `MicrosoftAppId`, `MicrosoftAppPassword`, `MicrosoftAppTenantId`). When they are all empty, as in the checked-in `appsettings.json`, the bot runs unauthenticated for local emulator use.

### 3. Expose the bot over HTTPS

For development, a dev tunnel is the quickest way:

```powershell
winget install Microsoft.devtunnel
devtunnel user login
devtunnel host -p 3978 --allow-anonymous
```

Put the printed `https://....devtunnels.ms/api/messages` URL into the Azure Bot's messaging endpoint (`az bot update ... --endpoint`).

For real use, deploy to an Azure App Service (Linux, B1). It gives a free HTTPS certificate on `*.azurewebsites.net`, stays awake so the Friday scheduler fires, and keeps `db/napoleon.db` on the persistent `/home` disk. One script does it all and is safe to rerun for every new deploy:

```powershell
./scripts/Deploy-Azure.ps1
```

It reads the bot credentials from user secrets (step 2), creates or updates the resource group, plan and site, sets HTTPS-only and Always On, publishes and zip-deploys the bot, points the Azure Bot's messaging endpoint at the site, and waits for `/healthz`. Use `-AppName`, `-Location` or `-Sku` to change the defaults, and `-SkipInfrastructure` to only redeploy code.

Avoid free tiers that sleep, and avoid SQLite on an Azure Files mount (Container Apps); the scheduler needs a process that is running at 12:00 on Friday and a local disk.

#### Or self-host with Docker

`Dockerfile` and `docker-compose.yml` run the bot on any Linux box with Docker. TLS and the public host name are left to whatever you already have in front of the machine, such as a Cloudflare Tunnel or a reverse proxy. The bot listens on `127.0.0.1:3978`.

```bash
git clone <this repo> && cd teams-napoleonkake-score
cp .env.example .env          # set the MicrosoftApp* values from step 1
docker compose up -d --build
curl http://localhost:3978/healthz   # -> ok
```

Point your tunnel or proxy at `http://localhost:3978`, then set the Azure Bot's messaging endpoint:

```bash
az bot update -g rg-napoleonskake -n napoleonskake-bot --endpoint "https://<your-host>/api/messages"
```

Notes:

- If `cloudflared` runs as a container in another compose project, it cannot see the host's localhost. Either run it with `network_mode: host`, or share a Docker network: uncomment the `networks` blocks in `docker-compose.yml`, `docker network create tunnel`, attach cloudflared to it, and target `http://napoleonskake-bot:8080`.
- The SQLite database is in the named volume `bot-db`. Back it up with `docker run --rm -v teams-napoleonkake-score_bot-db:/db -v $PWD:/backup alpine cp /db/napoleon.db /backup/`.
- To update: `git pull && docker compose up -d --build`. The volume survives rebuilds.
- Logs: `docker compose logs -f bot`.

### 4. Build and upload the Teams app package

```powershell
node scripts/make-icons.mjs                       # placeholder icons; replace with real ones any time
./scripts/Build-AppPackage.ps1 -BotId $appId      # writes appPackage/napoleonskake.zip
```

In Teams: **Apps → Manage your apps → Upload an app → Upload a custom app**, pick the zip, and add it to the team channel where the cake is discussed. If custom app upload is disabled in your tenant, a Teams admin can publish it to the org app catalog instead via the Teams admin center.

Once added, the bot posts a welcome message and remembers the channel for the Friday post.

## Configuration

| Setting                     | Default                     | Meaning                                              |
|-----------------------------|-----------------------------|------------------------------------------------------|
| `Schedule:Enabled`          | `true`                      | Turn the automatic Friday post on or off            |
| `Schedule:CakeDay`          | `Friday`                    | Which weekday gets cake                              |
| `Schedule:PostTime`         | `12:00`                     | Local time to post the card; scoring opens          |
| `Schedule:ScoringCloses`    | `17:00`                     | Local time scoring closes; card is locked            |
| `Schedule:PostResultsAtClose` | `true`                    | Post the final results card at closing time          |
| `Schedule:TimeZone`         | `Europe/Oslo`               | Time zone for the above                              |
| `Storage:ConnectionString`  | `Data Source=db/napoleon.db` | SQLite connection string                         |

## Ideas for more Friday features

The storage and card plumbing are generic enough to hang more on:

- **Cake of the month / year**: a monthly recap card posted the first Friday of the month.
- **Streaks and badges**: "5 Fridays in a row", "first to score", "always a 7".
- **Prediction game**: guess the average before scoring opens; closest guess wins bragging rights.
- **Other Friday polls**: quiz question, "who brought the best coffee", weekend plans vote. The `Input.ChoiceSet` card and the per-day upsert in `ScoreStore` generalise to any "one vote per person per Friday" game.
- **Photo evidence**: let people attach a picture of the slice to their score.
- **Friday soundtrack**: collect song suggestions into a playlist link.

## Notes

- One score per person per Friday; the user is identified by their Entra object id, so scoring from the channel or from a private chat counts as the same person.
- Bot Framework SDK 4.x is in maintenance mode; Microsoft's successor is the Microsoft 365 Agents SDK. The bot logic here is small and would port easily if that becomes necessary.
