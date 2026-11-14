namespace NetWasm.System.Security.Cryptography.Tests;

// Test keys, generated once with desktop .NET 11 for this suite (PKCS#8, unencrypted). They protect nothing.
internal static class CryptographyKeys
{
    internal const string Rsa = """
        -----BEGIN PRIVATE KEY-----
        MIIEvgIBADANBgkqhkiG9w0BAQEFAASCBKgwggSkAgEAAoIBAQCeSouU8zu5yNOL
        p8qh6cvUkfqGtgiQXjrB2Yf+I69F7hhK4klZAzaH12QgYDnM4A7aOdeOa6M8LdI/
        ywsedPni4kUGY8tJ1XPL+Bl1fLrhWkTW8gwtGKC0U5INEkhQgvLV5EsNQ8dsguLs
        9IHRz+2o5I6kGTcJwQci0fYcvDgyNOjVANsZLf2+mvUa3oM8VtgWssYY3myVrgO7
        YJwf6l81zbjdAZiFdIT3I+Z4TZ5LqVqBAyB1P3LrgLrq+fFnG60PY70an5WrEUvg
        +4+8hVhHfwGF7kZvI9IkV6kL0PIg7Z793Z0df17aQ1zaGiDRZv6VkYZ7Gv1E6yu/
        6FP6wLhjAgMBAAECggEABAPzF4O2E6fvCd9nSgUyyQEtAEbU38isJ+e7Reyk0RrR
        nfCwmKP2uapynUmjnblfVxTZjYDYgrnM2BZwAUVJY4KG6rsUWJqkFdwE1VClMvtF
        vMgmG+zAguKN1WwNzpEL1PY2Gy+Jewy08frf4jEqn7qwjaQgG8y8QBtgaWky6b8Y
        oxAuSw4+jDmoOhugqXQp0aSCYsXQcoinjo0GXsvAPOoVM2n3F/fywm+Ukw3b5G3k
        uXYS6RPz20Pbr3bjEu6PRBi7rsPNupZ4E37w+uF80GIOsjyGPdTZK3lGQO+hV5XO
        Jjxzd3K1XOcwsmJILRjOTPRxC5QedHZU4+qV/EReOQKBgQDPyofC147an25ZtDgO
        eTEatFM+HIkhzwPRK1v7AUo0yJWfpmhBb47swbP5FB9Vp9GxcplrNrCxkyD0rtL5
        AFA69QeZGtMuUiW6qTskftpbOEsrNDTuI0pgXwqTByqtLd6zxCwmmi/NPRnVFWt1
        kqf2GVo7tmPu0a22V8nhqD6VeQKBgQDDBAihf10jguzJofTyZsiJcbiba91NzVj0
        3CasFIVd/HPnZk8KkLGDNSdmGEbGjindW0hQIT6dvb17WT6GLC7QAn4NIfw3jT6d
        7UwWRMGYJGUtWjx3bGdDrfshiN0Ljo91zjI9eh8rbX7mTbtwv92JtyepnruEUCoU
        R/CS/WCRuwKBgQCOadv0YXLKSh0CpC8LVGqgR9kJp9sg7dZK5PHMrcyjMprB6fZo
        asJFaFkmLHFxbmycttOJlNkzS4h5ytxTxrA2SDvl5mm4xJqRPW2IjNyxSnrSaJyi
        XlOai2ke7fmE6fxJrvknl+ONQgRTc7PLyDEPzb53UIFbbCAn6jE2VO5X2QKBgQCM
        hD2L0G/WBKKFbtNGTlU04+I26vPc+ZedZ3veBjNK03rYQ1m0rSma9xPadqeYrhrw
        SrPCORIbKvp7BIUOvItGimU1JUXM6VMzDCcE7Knp7F+S3DAxhMSreZM4SJutlh08
        r0R4E6Sd8Bm97EihJz6D2psTlzJaVkDbuDJaV4PbvQKBgCk9jl+v+KnPHQhOcTP6
        JIqK0yFfy6+g2JceTkNIqCoacW/zJ/HyZcN/F9wEqSDqFS4dByIfi1gLflfEJmsQ
        k68Rr+laj3Yj6WT6dKmGDhw0BI/TtWLg4iREilxdu5zd4wIW6FqM8mnNga0f1rhV
        7T/8tdGzXekBXl2+eVAOz2t8
        -----END PRIVATE KEY-----
        """;

    internal const string OtherRsa = """
        -----BEGIN PRIVATE KEY-----
        MIIEvgIBADANBgkqhkiG9w0BAQEFAASCBKgwggSkAgEAAoIBAQCaavE54KFE7gtg
        kCpuZy2c6tKkXFtOYIxQnRrVr+YFnDFqsKBHmfKKBU0Lc9bfNU7kvGM17DGB2PhL
        wLaCB+VKL2PvYhfG55/gNguB4HNOXfCDnXN2wMJGnw09z+G0vwBTjb6J9K7BkTKx
        QIcmvUKcZS3kFlLx0MK0zq/YJNWyQRlaOm+UJnhpqRCV0LTVceO6XFf3EuCjHFrg
        uhYtryBpDgOlOlmDAoZtkn+FGkh7+siAo8X80ucYnuEMigWTBDNSLszq6Bplu+mA
        OHQJk2va8Y14TTz1y6VUo6Zx+6iAQyGHePt36hQTryQXdxNPux+Z5hEBRMIJZviz
        NbrUS83NAgMBAAECggEABfFiNc5HnqHDtdRukZ+ucXjXIQS97dsI42xcdUdnBKXB
        gB/qlz7dRwMTt0JezWndjq+SaOedcyggzDePPptjr3J59+NQU3MkI8qosDetcGFs
        qYAUisg54eJqrcuTnfNw5YZRxDjyxdfa+dfoK61ChEbfmITkbdKRP8ipm1m1kL3h
        12n93tCOP2cgXuKB6IstgersQrXtoJshvrN1Ytned+qxkV2SeW36/0e2GNMvU34e
        ztXk0YcNy3TKjIsU3RBnjpIcgrj6B0xwF9qk5qLP1NLxNwnYr+yC5smI7/Si57xW
        qK/0tOiLpx/8CXJoYgrCTrNza+wbxf4JCCAvA9wpIQKBgQDLIUiVrg6zAWrx1dCC
        +w3RCgM+kLDO+4T3P+DURHYyjMwrlpvnOwho+kzmynH2/P+cKa1N3M6nsFdzuz/W
        ENerNeIoiXq3/yyOASeWSyZ2CdjvGgZeKBnmTSzRfyf9q0L4cgkG2nYuslikTAJH
        zJ18BwSIoDfHR+UcQqhSTqq67QKBgQDCm+vCBBe2y79frm3ICaD9vtgAsoMFZTyq
        kOscrlqReCo/X+3OxROForNNNkB4ztbOj63OrzDMIadqvwUTZ437SVJK05zCAgfr
        y4UA1VEgyW5HIkK8n1dHloe7zb9oUF7XmsrUyMQl9y8NIsY86hhZ3ahnLVxHgKUa
        dpHPIXWiYQKBgQCP4Wp9g0WsK8Ppuo6wSE3YFKSJq4lhgy0CyT9ie3/RfIOeGFcj
        6V+ipDDWhO1GipwvrCGydkX252R6eFbWNSXegxNHrWG8dMXFbDHzNhXvJDiB3EhI
        Z1U5GM83MT9NYgLU4klcjl+J4hABiElRPDEMyhe/AuWfhWBWrSx26HvsXQKBgQCT
        TwM+cWqh68iI3Lgs210JthQ7MuUKiH9LfTb7Ft9njdYs8qhIA9WrKmUe5RoYWDkx
        UoD2GpaNbkwT+VQWRCStOesYyOW31WG5kuyynMY0U0UugziRHuwqPD+yYSOqCmxA
        N+Og/fe75UewYco9AdY88BQxRZy8YxyW03m7mFA7AQKBgFOwL6J/cv7yOmhIf4IR
        KMeSiwDFxFxpS+Crm+R409wuw40mW0hy8OvVPy+9xc0QmBfPy19X67JCcsKkHtO2
        230hmPDH5vYYHWMtZm9tdoFSdyrHVYhk+PHmuNtzrL2r/r3baujZfEghLG3OOBPJ
        n+lNQv0oXtajD++U0KgWWggf
        -----END PRIVATE KEY-----
        """;

    internal const string EcDsa = """
        -----BEGIN PRIVATE KEY-----
        MIGHAgEAMBMGByqGSM49AgEGCCqGSM49AwEHBG0wawIBAQQgjh+nwy9mxD4xLWWM
        6qR/2jYLdV2Ag2v7Vald5Tw7JlGhRANCAARD1IlbfhmqTXH5D4HKE3BWTqiQl0T0
        ldHShpclHv3Ib/21FqItLipHkSfGlFKaR9uexBM6MUAmO2JRLiHjbBiA
        -----END PRIVATE KEY-----
        """;

    internal const string EcDh = """
        -----BEGIN PRIVATE KEY-----
        MIGHAgEAMBMGByqGSM49AgEGCCqGSM49AwEHBG0wawIBAQQgSXutgN9O2t2BGY6m
        d3Z9T0fTa8hH1wmJAQgjgX20LoyhRANCAAR4SXZM0WkPtCZj/7lke2LGKdJXKfzW
        svfyhhvIbi+2qKek3TYXtVxOvdkW1rbshbKc1P0GgDO7amFGgAM3pHB3
        -----END PRIVATE KEY-----
        """;

    internal const string Dsa = """
        -----BEGIN PRIVATE KEY-----
        MIICZAIBADCCAjkGByqGSM44BAEwggIsAoIBAQDcZFpy6Zza0ltMQnVqPLtTSFh9
        5kmi+ioX8KPmT3ThgxvFid/eBDBXNCmCqVx04NSE7XUy5l75IAerlM5UJnGdck9n
        6h33aG95QycEUCx0q3GItk7TZs94u5FTbfJXbFZeM0iaXqFCGYyDQI/1WLqAhYbl
        l6Gf/8za4MKSdPOLEvmGCDaJtpQ3BWoVpAsDebfM8eoSN24ClgnwmR1OBL1Pu37U
        b9vxM/4B3CMhxrwuiIZ3YaInx2cTVgET5G2dnXkpikr4foClGGxRPCjzCQiwDNrL
        DwkeZ04NLoGgoJ9OAnd4uchVw4p5RXKHGIaQoF1caktRR+tQp/TA2y1PFNOBAiEA
        5mTHZ0nhEPXvQLEy9wrdc8tfUTsW5VDBwmW3zwLnMF8CggEAOw7wq0DVbJhe3c5M
        s28D+6sQdsKOKotWmDX44Eo2aYIrhpMQk6MSDLiSry8sx8or9eJdKuAEfXGFQCK/
        WW3xNwPl7CBBTXg5tkBQvBE28uWyWljiU8pW98e9ID7CamcOvijk+cwCaQ8f1WPu
        EPl4tDstqYrD/MSmdxAIVSjvPAUC/4LKK9cSvXslC+bxkQrdwb+0I8z50JRdvq7s
        d5D4fSViL11rGgdrsQWpUMMPc57K/hyrouFXU5Bt5ajTTLpDrfU99syHKTCZ8/DZ
        F1GnfJngqgxl2MBbUsMy2wrHOsvH0J4RJP2mnC/tK1DYsx647ayukvgY9SBHxWSg
        X6UDCgQiAiAbCJo64ZH8L8wfC5WQ1HKlAfWwzv4Z4+9NX6dQWEOEqQ==
        -----END PRIVATE KEY-----
        """;
}
