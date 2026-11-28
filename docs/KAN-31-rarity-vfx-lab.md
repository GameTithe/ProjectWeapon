# 등급별 공격 VFX 비교

한 던전에서 같은 유랑 검사 공격 동작을 다섯 번 나란히 재생한다. 샘플은 게임의 적색 공격 예고, 푸른 귀환진, 체력과 시계 표시를 관찰 기준으로 배치한다. 검증용 시야와 판정을 공유하고 등급별 색과 잔상, 파편의 밀도를 바꾼다. 타격 범위나 게임플레이 수치는 이 씬이 정하지 않는다. [KAN-31](https://gametithe.atlassian.net/browse/KAN-31) · [KAN-21](https://gametithe.atlassian.net/browse/KAN-21) · [KAN-22](https://gametithe.atlassian.net/browse/KAN-22)

| 등급 | 대표색 | 시각 기준 |
| --- | --- | --- |
| 일반 | `#C0C6CE` | 짧고 가는 은빛 초승달과 두 파편 |
| 고급 | `#75D594` | 녹색의 넓어진 날과 흩어지는 점 |
| 희귀 | `#7ABFFF` | 푸른 외곽과 가느다란 안쪽 잔상 |
| 영웅 | `#CF9EFA` | 자주색 층을 이룬 날, 십자 번쩍임과 파편 |
| 전설 | `#FFD273` | 황금빛 중심선, 넓은 잔상, 가장 많은 파편 |

원본은 `scripts/generate-kan31-vfx.mjs`로 재생성할 수 있는 자체 제작 래스터다. 사용자 컨셉의 암보라 폐성, 횃불, 붉은 예고와 푸른 귀환진, KAN-196 유랑 검사 공격 스프라이트를 참고했다. 원본 880×440 RGBA 시트에 다섯 등급 × 다섯 프레임의 기본 및 감소 프레임을 담았다. 32 PPU와 Point 필터를 사용하며 월드 카메라의 기준 해상도는 480×270이다. 72ms 간격으로 한 번 재생하고 360ms에 숨긴다. 씬 시작에서 준비한 렌더러 다섯 개만 재사용한다. [KAN-196](https://gametithe.atlassian.net/browse/KAN-196)

## 재현

Unity에서 `Assets/Scenes/VfxRarityLab/VfxRarityLab.unity`를 열고 Play한다. Space는 다섯 타격 재생, 숫자 1~5는 개별 등급 재생, R은 감소 프리셋이다. 독립 씬은 제품 빌드의 공유 씬 목록에 등록하지 않는다.

```powershell
node scripts/generate-kan31-vfx.mjs
$vfxBuild = Join-Path $env:TEMP 'KAN-31-proof'
New-Item -ItemType Directory -Path $vfxBuild -Force | Out-Null
unity build . --target StandaloneWindows64 --execute-method ProjectWeapon.VfxRarityLab.Editor.VfxRarityLabBuilder.BuildWindows --output "$vfxBuild/VfxRarityLab.exe"
$vfxPlayer = Join-Path $vfxBuild 'VfxRarityLab.exe'
foreach ($width in 960, 1170) {
    & $vfxPlayer -force-d3d11 -screen-fullscreen 0 -screen-width $width -screen-height 540 -kan31-report "$vfxBuild/${width}x540" | Out-Null
    Get-Process VfxRarityLab -ErrorAction SilentlyContinue | Where-Object Path -EQ $vfxPlayer | Wait-Process
}
```

`-kan31-report` 실행은 준비 후 기준, 기본, 감소 모드마다 1.5초 워밍업과 4초의 연속 프레임을 수집한다. 기준도 같은 유랑 검사 동작을 재생하며 VFX 표시만 생략한다. 각 비교에서 650ms마다 같은 공격을 시작한다. VSync와 프레임 상한을 해제하고 Player의 프레임 간격 P50/P95/P99를 기록한다. PNG와 화면 점유는 타이밍 기록 이후에 캡처한다.

점유율은 같은 자세·배경의 기준 프레임과 공격 프레임을 같은 카메라에서 읽어, 한 채널 이상의 8비트 RGB 차이가 4 이상인 픽셀 수를 실제 화면 전체 픽셀 수로 나눈 값이다. 등급별로 다섯 타격 프레임을 시험하여 가장 큰 변화 픽셀 수를 기록한다. 동일한 방법으로 다섯 등급을 한꺼번에 표시한 최대치도 기록한다. 이는 화면에서 바뀐 픽셀의 점유를 뜻한다. 숨긴 창처럼 내용이 렌더링되지 않으면 측정을 중단한다. [Unity 화면 캡처](https://docs.unity.com/en-us/engine/6000.6/script-reference/unityengine/screencapture/capturescreenshotastexture) · [Unity 백그라운드 실행](https://docs.unity.com/en-us/engine/6000.6/script-reference/unityengine/application/runinbackground)

## Windows 결과

Unity 6000.6.9f1 Windows x64 Player, Direct3D 11, Ryzen 7 7800X3D, RTX 5070에서 측정했다.

| 카메라 화면 | 기본 전체 최대 점유 | 감소 전체 최대 점유 | 기준/기본/감소 P95 | 기준/기본/감소 P99 |
| --- | ---: | ---: | --- | --- |
| 960×540 | 8,048px / 1.5525% | 7,488px / 1.4444% | 0.316 / 0.313 / 0.328ms | 1.355 / 1.345 / 1.361ms |
| 1170×540 | 8,048px / 1.2738% | 7,488px / 1.1852% | 0.497 / 0.492 / 0.492ms | 3.664 / 3.630 / 3.647ms |

| 등급 | 960×540 기본 → 감소 | 1170×540 기본 → 감소 |
| --- | ---: | ---: |
| 일반 | 0.1790% → 0.1790% | 0.1469% → 0.1469% |
| 고급 | 0.2307% → 0.2307% | 0.1893% → 0.1893% |
| 희귀 | 0.3094% → 0.3094% | 0.2539% → 0.2539% |
| 영웅 | 0.3843% → 0.3310% | 0.3153% → 0.2716% |
| 전설 | 0.4491% → 0.3943% | 0.3685% → 0.3235% |

두 화면에서 HUD와 푸른 귀환진이 읽힌다. 타격은 적의 외곽과 붉은 예고 일부를 스치며, 붉은 예고의 방향은 남는다. 감소 프리셋은 전체 변경 픽셀을 약 7% 줄이고 1~3등급과 주된 초승달 윤곽을 보존한다. 이 장면의 짧은 프레임 샘플에서 일관된 데스크톱 속도 향상은 확인되지 않았다. 기준 화면과 등급별 피크를 계산할 때 사용한 실제 Windows 캡처 및 CSV는 프로젝트 Obsidian의 KAN-31 기록에 있다. 10회 재생 후에도 활성화 가능한 이펙트 슬롯은 5개, 전체 스프라이트 렌더러는 16개였다.

## 조정안

- 가독성과 프레임 예산이 부족하면 감소 프리셋에서 영웅·전설의 내부 잔상과 일부 스파크를 줄인다. 주된 초승달 윤곽과 등급색은 남긴다. 과다 동시 재생에서는 가장 가까운 타격을 먼저 유지하고 나머지 잔상 재생을 제한한다. 저사양 모드만 배포하는 빌드에서 쓰지 않는 프레임을 별도 아틀라스로 분리하면 메모리도 줄일 수 있다.
- 최소 Android API 29·4GiB와 iOS 15·A13 실기기에서 최종 1구역 빌드의 30 FPS 목표, P95/P99, 장시간 열 상태와 16:9/와이드 HUD 가독성을 다시 측정한다. Windows 샘플은 비교 기준이며 실제 보장 기기 판정은 KAN-17 절차를 따를 예정이다. [KAN-17](https://gametithe.atlassian.net/browse/KAN-17)
