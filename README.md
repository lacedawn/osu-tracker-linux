# circle-tracker

this is practically the only part written by a human

![Circle Tracker Preview](assets/circletrackerlazer.png)

## what changed
- rewritten with Avalonia UI on .NET 8 (tested on Arch Linux, untested on Windows)
- uses [tosu](https://github.com/tosuapp/tosu) for memory reading
- works with osu!lazer

## setup

### 1. install tosu
On Arch:
```bash
yay -S tosu
sudo setcap cap_sys_ptrace=eip /opt/tosu/tosu
```

### 2. run
```bash
dotnet run
```

## credits
- [FunOrange](https://github.com/FunOrange) for the original circle-tracker
- design inspired by [osu-trainer](https://github.com/FunOrange/osu-trainer)
- [tosu](https://github.com/tosuapp/tosu) for the memory reader

  tosu isn't available on macos so the release is useless 
