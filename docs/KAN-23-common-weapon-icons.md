# KAN-23 일반 무기 5종 픽셀 아이콘

`Assets/ProjectWeapon/Art/CommonWeaponIcons/`의 32×32px 투명 스프라이트다. Unity Sprite Single, 32 PPU, Point 필터를 사용한다. 원본 `art-source/Export-CommonWeaponIcons.ps1`로 재생성하며 외부 원본에 의존하지 않는다. 낡은 검은 유랑 검사 장착 아트의 강철 검날과 갈색 손잡이를 유지한다. [KAN-23](https://gametithe.atlassian.net/browse/KAN-23) · [KAN-21](https://gametithe.atlassian.net/browse/KAN-21) · [KAN-196](https://gametithe.atlassian.net/browse/KAN-196)

| 무기 | Sprite | 작은 화면에서 보이는 특징 |
| --- | --- | --- |
| 낡은 검 | `WornSword` | 길고 곧은 은색 날과 갈색 가드 |
| 나무 창 | `WoodenSpear` | 긴 나무 자루와 짧은 창날 |
| 사냥 활 | `HuntingBow` | 휘어진 나무, 옅은 시위와 빈 중심 |
| 철퇴 | `Mace` | 둥근 돌기 머리와 갈색 자루 |
| 단검 | `Dagger` | 짧고 폭이 넓은 날과 손잡이 |

아이콘을 Unity UI에 넣을 때 `Image.sprite`로 지정하고 종횡비를 유지한다. 24×24px에서 검토했으며 원본 32×32px 또는 정수 배율을 기본으로 사용한다. 아이콘은 별도 UI 참조로 제공한다.
