"""Builds Theme/worldmap/markers.csv for the world map from the shard's own data (ModernUO Distribution):

- towns: each TownRegion's name (not the farms' fields), at its go location (else the middle of its largest area)
- moongates: PublicMoongate's destinations (Felucca, Ilshenar, Malas)
- dungeons: each DungeonRegion with an entrance, at the entrance
- shops: every vendor spawner, by trade, one marker per trade within a few tiles

Run with the shard checkout's Distribution folder:  python make_worldmap_markers.py <Distribution> <out.csv>
Columns: map,x,y,kind,label (map: 0 Felucca, 2 Ilshenar, 3 Malas; kind: town, moongate, dungeon, or a shop kind).
"""
import csv
import glob
import json
import os
import sys

MAPS = {"Felucca": 0, "Ilshenar": 2, "Malas": 3}

# Vendor type -> (kind, label). The kind picks the icon in WorldMapWindow.
TRADES = {
    "Banker": ("bank", "Bank"), "Minter": ("bank", "Bank"),
    "Healer": ("healer", "Healer"), "HealerGuildmaster": ("healer", "Healer"),
    "Mage": ("mage", "Mage shop"), "MageGuildmaster": ("mage", "Mage shop"),
    "Alchemist": ("alchemist", "Alchemist"), "Herbalist": ("alchemist", "Herbalist"),
    "Blacksmith": ("smith", "Smith"), "BlacksmithGuildmaster": ("smith", "Smith"),
    "Weaponsmith": ("smith", "Weapons"), "Armorer": ("smith", "Armour"),
    "InnKeeper": ("inn", "Inn"), "TavernKeeper": ("inn", "Tavern"), "Barkeeper": ("inn", "Tavern"),
    "Provisioner": ("provisioner", "Provisioner"),
    "Baker": ("food", "Baker"), "Butcher": ("food", "Butcher"), "Cook": ("food", "Cook"), "Farmer": ("food", "Farmer"),
    "Tailor": ("tailor", "Tailor"), "TailorGuildmaster": ("tailor", "Tailor"), "Weaver": ("tailor", "Weaver"),
    "Tanner": ("tailor", "Tanner"), "Furtrader": ("tailor", "Fur trader"),
    "AnimalTrainer": ("stable", "Stable"), "Rancher": ("stable", "Rancher"),
    "Shipwright": ("docks", "Shipwright"), "Fisherman": ("docks", "Fisherman"), "FisherGuildmaster": ("docks", "Fishermen's guild"),
    "HarborMaster": ("docks", "Harbour master"),
    "Mapmaker": ("map", "Mapmaker"),
    "Bowyer": ("bowyer", "Bowyer"), "RangerGuildmaster": ("bowyer", "Rangers' guild"),
    "Tinker": ("tinker", "Tinker"), "TinkerGuildmaster": ("tinker", "Tinker"),
    "Carpenter": ("carpenter", "Carpenter"), "Architect": ("carpenter", "Architect"), "RealEstateBroker": ("carpenter", "Real estate"),
    "Jeweler": ("jeweler", "Jeweler"),
    "Scribe": ("scribe", "Scribe"),
    "Bard": ("bard", "Bard"), "BardGuildmaster": ("bard", "Bards' guild"),
    "MinerGuildmaster": ("miner", "Miners' guild"),
    "WarriorGuildmaster": ("warrior", "Warriors' guild"),
}

# PublicMoongate.cs destinations for the facets the shard runs.
MOONGATES = [
    (0, 4467, 1283, "Moonglow"), (0, 1336, 1997, "Britain"), (0, 1499, 3771, "Jhelom"), (0, 771, 752, "Yew"),
    (0, 2701, 692, "Minoc"), (0, 1828, 2948, "Trinsic"), (0, 643, 2067, "Skara Brae"), (0, 3563, 2139, "Magincia"),
    (0, 2711, 2234, "Buccaneer's Den"),
    (2, 1215, 467, "Compassion"), (2, 722, 1366, "Honesty"), (2, 744, 724, "Honor"), (2, 281, 1016, "Humility"),
    (2, 987, 1011, "Justice"), (2, 1174, 1286, "Sacrifice"), (2, 1532, 1340, "Spirituality"), (2, 528, 216, "Valor"),
    (2, 1721, 218, "Chaos"),
    (3, 1015, 527, "Luna"), (3, 1997, 1386, "Umbra"),
]

MERGE = 6  # tiles: spawners of one kind this close share a marker


def largest(areas):
    best = max(areas, key=lambda a: (a["x2"] - a["x1"]) * (a["y2"] - a["y1"]))
    return (best["x1"] + best["x2"]) // 2, (best["y1"] + best["y2"]) // 2


def town_at(towns, m, x, y):
    for t in towns:
        if t["map"] == m and any(a["x1"] <= x < a["x2"] and a["y1"] <= y < a["y2"] for a in t["areas"]):
            return t["name"]
    return None


def main(dist, out):
    regions = json.load(open(os.path.join(dist, "Data", "regions.json"), encoding="utf-8"))
    rows, towns = [], []
    for r in regions:
        m = MAPS.get(r.get("Map"))
        if m is None or not r.get("Area"):
            continue
        kind = r.get("$type")
        if kind == "TownRegion" and r.get("Name"):
            go = r.get("GoLocation")
            x, y = (go["x"], go["y"]) if go else largest(r["Area"])
            if "field" in r["Name"].lower():
                continue  # the farms' crop fields are town regions too
            towns.append({"map": m, "name": r["Name"], "areas": r["Area"]})
            if x >= 5120 and m == 0:
                continue  # the dungeon / Lost Lands half of the map file; towns there are not on the world map
            rows.append((m, x, y, "town", r["Name"]))
        elif kind == "DungeonRegion" and r.get("Name") and r.get("Entrance"):
            e = r["Entrance"]
            if m == 0 and e["x"] >= 5120:
                continue
            rows.append((m, e["x"], e["y"], "dungeon", r["Name"]))
    for m, x, y, name in MOONGATES:
        rows.append((m, x, y, "moongate", name + " moongate"))

    shops = []
    for path in glob.glob(os.path.join(dist, "Data", "Spawns", "*", "*", "Vendors.json")):
        for sp in json.load(open(path, encoding="utf-8")):
            m = MAPS.get(sp.get("map"))
            if m is None:
                continue
            x, y = sp["location"][0], sp["location"][1]
            for e in sp.get("entries", []):
                trade = TRADES.get(e.get("name"))
                if trade:
                    shops.append((m, x, y) + trade)
    kept = []
    for m, x, y, kind, label in shops:
        if any(k[0] == m and k[3] == kind and abs(k[1] - x) <= MERGE and abs(k[2] - y) <= MERGE for k in kept):
            continue
        kept.append((m, x, y, kind, label))
    for m, x, y, kind, label in kept:
        town = town_at(towns, m, x, y)
        rows.append((m, x, y, kind, f"{town} {label.lower()}" if town and kind != "bank" else (f"{town} bank" if town else label)))

    os.makedirs(os.path.dirname(out), exist_ok=True)
    with open(out, "w", newline="", encoding="utf-8") as f:
        w = csv.writer(f)
        w.writerow(["map", "x", "y", "kind", "label"])
        w.writerows(sorted(set(rows)))
    print(len(rows), "markers;", len(kept), "shops;", len(towns), "towns")


if __name__ == "__main__":
    main(sys.argv[1], sys.argv[2])
